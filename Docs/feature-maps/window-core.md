# Feature map — Window core engine

**Folders:** `Assets/Scripts/UI/Core/**`, `UI/Definitions/**`, `UI/Infrastructure/**`,
`UI/Runtime/**` (except `Runtime/Widgets/**` and the icon provider in `Runtime/Content/`),
`UI/Transitions/**`, `UI/Services/IRemoteImageLoader.cs`/`RemoteImageLoader.cs`, plus the
presentation port in `Assets/Scripts/Contracts/**` (`WindowType`, `WindowLifecycleState`,
`IWindowData`, `EmptyWindowData`, `IWindowsManager`, `WindowHandle` — assembly
`PopupSystem.Contracts`)
**Depends on:** `Zenject` (typed factories only — `PrefabFactory<WindowView>` in `WindowFactory`,
`IFactory<TController>` in each module; no class here injects `DiContainer`), `Addressables`,
`Contracts`; nothing from `Game` in the engine itself (the `UI` assembly references `Game` and
TextMeshPro for the concrete windows, widgets and `Services/RewardIcons`, not for the engine)
**Depended on by:** `App` (wiring), every concrete window under `UI/Windows/**`,
`Game.Services.WindowQueue` (drives it, but only through `IWindowsManager`/`WindowHandle` — and it
only ever sees `PopupSystem.Contracts`, never `PopupSystem.UI`)
**Related knowledge base:**
[`unity-ui-canvas-and-input.md`](../knowledge-base/unity-ui-canvas-and-input.md) (canvas topology,
draw and input order — the measured behaviours behind `UILayerSorter`),
[`pooling-and-ownership.md`](../knowledge-base/pooling-and-ownership.md) (pooling traps, texture
ownership, the close-ordering constraints),
[`addressables-and-content.md`](../knowledge-base/addressables-and-content.md) (the content seam),
[`unitask-and-cancellation.md`](../knowledge-base/unitask-and-cancellation.md) (which token the open
and close paths get)

## Purpose

The generic, window-type-agnostic machinery that satisfies "Management & Sequencing" +
"Lifecycle Control" + "Transition System" + "Input & Modality" from the assessment brief. Nothing
in this folder set knows what `MainGame` or `Offer` are — every window-specific fact is injected
from outside via `IWindowModule`.

## The pieces, and how a call flows through them

```
IWindowsManager.OpenAsync(type, payload)
        |
        v
await WindowFactory.CreateAsync(request, ct)
        |                      -- via IWindowRegistry.Get(type) for the WindowDefinition
        |                         via IUILayerProvider.GetLayer(definition.Layer) for the parent
        |                         pops a pooled WindowView, or awaits IUiPrefabProvider.LoadAsync
        |                         (Addressables) and instantiates via PrefabFactory<WindowView>
        |                         via IWindowControllerResolver.Create(type) for the controller
        |                         then assigns the transition and hides the view
        v
WindowInstance (Type, Definition, View, Controller, LifetimeCts)
        |
        v
WindowsManager pushes it onto the right stack (base / windowStack / popupStack),
tells ModalBackdropPresenter.Refresh(...), then:
        |
        v
WindowHandle.SetState(Initializing) -> Controller.InitializeAsync(view, handle, payload, ct)
        |
        v
WindowHandle.SetState(Opening)      -> View.Show() -> UILayerSorter.Apply(layer)
                                    -> View.PlayOpenAsync(ct)  [defers to IWindowTransition]
        |
        v
WindowHandle.SetState(Active)
```

Closing (`WindowsManager.CloseInstanceAsync`) runs the mirror image with a *non-cancellable*
token (`Closing` must always reach `Disposed`): add the handle to `_closingHandles`, unsubscribe
the close event, cancel the instance's lifetime token, `SetState(Closing)`,
`View.PlayCloseAsync(CancellationToken.None)` (a throw is logged), `Controller.Dispose()`,
`WindowFactory.Release(instance)` (pool, don't destroy), dispose the lifetime token, remove the
handle from `_closingHandles`, `Handle.MarkClosed()` → `SetState(Disposed)`, raise
`QueueBecameIdle` if now idle, refresh backdrops.

**Observer and controller code cannot break the teardown.** Each `StateChanged` subscriber is invoked
on its own and a throw is logged (`Debug.LogException`), so it neither reaches the other subscribers
nor the manager; `MarkClosed` completes `WaitForCloseAsync` in a `finally`. On both the close and the
`Dispose` paths, a throw from the lifetime token's callbacks, `Controller.Dispose()` or
`WindowFactory.Release` is logged and the rest still runs — the view is pooled, the handle leaves
`_closingHandles`, `MarkClosed` runs and backdrops are refreshed (after `QueueBecameIdle`, in a
`finally`). Before this, a throwing `Disposed` subscriber left `IsClosed == true` with
`WaitForCloseAsync` pending forever, and a throwing `Closing` subscriber left the handle in
`_closingHandles`, so the queue never read idle again.

## Key files

| File | Responsibility |
|---|---|
| `Contracts/WindowType.cs` | The closed set of window identities. Adding a window = adding a value here. |
| `Contracts/WindowLifecycleState.cs` | `None → Initializing → Opening → Active → Closing → Disposed`, enforced by `WindowHandle.SetState`: a repeat or a backward move is ignored, skipping forward is allowed (see Gotchas). |
| `Definitions/UIEntryKind.cs` / `UILayerType.cs` | `Window` vs `Popup` (which stack it lives on) and which `UIRoot` layer transform it parents under. |
| `Contracts/IWindowData.cs`, `Contracts/EmptyWindowData.cs`, `Core/WindowRequest.cs` | The payload contract. `IWindowData` is a pure marker; `EmptyWindowData` is the shared "no payload" struct; `WindowRequest` bundles type+payload for the factory. |
| `Definitions/WindowDefinition.cs` | Immutable per-type config: `Type`, `Kind` (`UIEntryKind`), `Layer`, `IsModal`, `ViewType`, `PrefabAddress` (an Addressables address, e.g. `UI/Windows/OfferWindow`), `Transition` (`IWindowTransition`), `CloseOnBackdropClick`, `IsBaseScreen`. |
| `Runtime/Content/IUiPrefabProvider.cs`, `AddressablesUiPrefabProvider.cs` | The content seam. `LoadAsync(address, ct)` loads and caches an `AsyncOperationHandle<GameObject>` per address; `GetLoaded(address)` is the synchronous lookup for something already preloaded (the modal backdrop); `LoadBlocking(address)` is the one deliberate `WaitForCompletion` call, used only by `AppInstaller` for the preloader overlay, which by definition has to exist before anything can be awaited on screen. Implements `IDisposable` and releases every handle it took. |
| `Infrastructure/UIRoot.cs`, `IUILayerProvider.cs` | `UIRoot` is the `MonoBehaviour` implementing `IUILayerProvider` with the four layer `Transform`s (`Windows`/`Popups`/`Notifications`/`System`). The engine depends only on `IUILayerProvider.GetLayer(UILayerType)`, never on `UIRoot`, so a test supplies layers without a scene (`TestUiHierarchy`). |
| `Infrastructure/UILayerSorter.cs` | Static `Apply(layer)`: turns a layer's sibling order into child-canvas `sortingOrder` (layer canvas order + index + 1, `overrideSorting` on). See "Canvas topology" below. |
| `Runtime/IWindowModule.cs` | One `WindowDefinition` + `CreateController()` per window type; multi-bound in `AppInstaller`. This is *the* extensibility seam — see `Docs/architecture.md` §6. |
| `Runtime/Registry/IWindowRegistry.cs`, `WindowRegistry.cs` | `Dictionary<WindowType, WindowDefinition>` built once from every bound `IWindowModule`. Fully generic. |
| `Runtime/ControllerResolver/IWindowControllerResolver.cs`, `WindowControllerResolver.cs` | Same pattern for `IWindowController` creation. Fully generic. |
| `Runtime/Factory/IWindowFactory.cs`, `WindowFactory.cs` | `CreateAsync` — awaits `IUiPrefabProvider` for the prefab, instantiates it through Zenject's `PrefabFactory<WindowView>`, pools up to 2 closed `WindowView`s per `WindowType` (`MaxPooledViewsPerType`) in a `Stack<WindowView>` instead of destroying them (extras, and anything released after `Dispose`, are destroyed); a reused view is re-parented and moved to last sibling (a fresh one is also stretched to its layer). If anything after the view is acquired throws (a missing controller binding, a controller constructor), the view goes back through the same path as `Release` — reset, hidden, pooled or destroyed — any controller/token already created is disposed, and the original exception is rethrown. **No code-built fallback** — an unconfigured or unresolvable address is a hard `InvalidOperationException`. |
| `Runtime/Manager/WindowsManager.cs` | The orchestrator: three logical stacks (`_baseWindow` for `MainGame`, `_windowStack`, `_popupStack`), open/close sequencing, abort-on-cancel-during-open handling, delegates modality to `ModalBackdropPresenter`. `IsQueueIdle` (both stacks empty, nothing closing **and no open still waiting on its prefab load** — `_pendingOpens`) is what gates the queue, and `QueueBecameIdle` is the event it raises when that flips back to true — the queue listens rather than polls. |
| `Contracts/WindowHandle.cs` | The public handle callers hold: `CloseAsync()`, `WaitForCloseAsync()`, `State`/`StateChanged`, `IsClosed`. `SetState`/`MarkClosed` are `internal` to `PopupSystem.Contracts` — only `WindowsManager` (and the EditMode fake) drive them, via an `[InternalsVisibleTo]` that names those two assemblies. Lives in `Contracts` rather than `UI/Runtime` because the queue, which is in `Game`, holds handles. |
| `Runtime/WindowInstance.cs` | Internal bookkeeping record (`WindowsManager`'s own view of an open window) — not exposed to callers. |
| `Runtime/WindowView.cs` | Abstract `MonoBehaviour` base: `Show()`/`Hide()`, `PlayOpenAsync`/`PlayCloseAsync` (defer to the assigned `IWindowTransition`, if any), `RequestClose()` for concrete views to raise, `ResetForPool()` hook for pooled per-open state cleanup. |
| `Runtime/Controller/IWindowController.cs`, `WindowController.cs` | `IWindowController` (`IDisposable` + `InitializeAsync`) and its abstract generic base (`TData : IWindowData`, `TView : WindowView`) — casts the view, converts/validates the payload, exposes `View`/`Handle` to subclasses, delegates to `OnInitializeAsync(TData, ct)`. |
| `Runtime/Backdrop/ModalBackdropPresenter.cs` | Per-layer dimming/input-blocking scrim: finds the topmost active instance per layer, shows/hides/parents a pooled backdrop `GameObject` behind it, wires backdrop-tap-to-close only when `WindowDefinition.CloseOnBackdropClick` is true. |
| `Transitions/IWindowTransition.cs`, `FadeScaleWindowTransition.cs`, `SharedWindowTransitions.cs` | Pluggable open/close animation strategy. `FadeScaleWindowTransition` is a dependency-free per-frame `CanvasGroup` alpha + uniform-scale lerp; `SharedWindowTransitions.PopupDefault` is a shared stateless instance most windows reuse. |
| `Services/IRemoteImageLoader.cs`, `RemoteImageLoader.cs` | Downloads a `Texture2D` via `UnityWebRequestTexture`, resolving relative keys against a configurable base URL (see `Docs/architecture.md` §7). Not window-specific, but lives here because it's a UI-layer concern (popups display images; `Game` doesn't). |

## Canvas topology and draw order

The UI is not one canvas: `UIRoot`'s four layers each carry their own `Canvas` + `GraphicRaycaster`
(sorting bands 1000/2000/3000/4000 in `MainScene`), and so does every window prefab and
`ModalBackdrop`. Sibling index stays the single source of truth for stacking, and
`UILayerSorter` turns it into the `sortingOrder` that separate canvases need in order to both draw
*and* raycast in that order, after every open and close.

The full reasoning — why the canvas was split, the two things that split gives away, the
`overrideSorting`-while-inactive behaviour that forces `WindowsManager` to order a window twice, the
layer bands and raycast hygiene — is in
[`unity-ui-canvas-and-input.md`](../knowledge-base/unity-ui-canvas-and-input.md). Read it before
touching `UILayerSorter`, `UIRoot`, the ordering calls in `WindowsManager`, or a window prefab's
root.

## Modality / backdrop model

A window's `IsModal` flag (in its `WindowDefinition`) is the only thing that decides whether it
gets a backdrop. `ModalBackdropPresenter.Refresh` is called after every stack mutation
(open/close/abort) with the current set of active instances; it computes, per layer, whichever
instance is topmost (`GetSiblingIndex()`), shows a backdrop behind it if it's modal, hides any
backdrop whose layer no longer has an active modal on top. Backdrop tap-to-dismiss is opt-in per
window (`CloseOnBackdropClick`) — defaults to `false` so an engagement/monetization popup is never
lost to an accidental tap. Input blocking during transitions is handled independently, by the
transition itself flipping `CanvasGroup.blocksRaycasts`/`interactable` off the instant closing
starts (see `FadeScaleWindowTransition.PlayCloseAsync`) — a window can't be "clicked through"
while it's visibly fading out.

## Decisions specific to this subsystem

**`IsBaseScreen` is a flag, not a `WindowType` check.** Three behaviours follow from it, and none
of them may be spelled `type == WindowType.MainGame` inside `WindowsManager`: the window is
single-instance (opening it again hands back the handle of the one already up rather than stacking a
second copy behind it), it lives in its own slot rather than on the window stack, and it does not
count as "the screen is busy" for the queue — the whole point of a base screen is that the queue
runs on top of it. Making it a flag is what makes the README's extensibility claim literally true:
the engine names no concrete window. The base-screen slot is *overwritten* rather than required to
be empty, because it can only ever hold a closed instance at that point (a live one was handed back
above) — pushing a reopened base screen onto the window stack instead would leave it permanently
counting as "busy".

**Concurrent base-screen opens share one in-flight open.** Without that, two `OpenAsync(MainGame)`
calls made before the first prefab load finishes would both find the base slot empty and each
instantiate a copy; the one overwritten in `_baseWindow` would stay on screen, on no stack,
unreachable by its handle. `WindowsManager` keeps the running base-screen open in `_baseScreenOpen` (a
`UniTaskCompletionSource<WindowHandle>`), checked *before* the base slot, so a concurrent caller
gets exactly what the first gets — the same handle once it is `Active`, or the same exception. Every
caller, the first included, awaits that source, so a failure is always observed and never reported as
unobserved. The field is cleared before the source completes, so a caller resumed by it that opens
again sees the base slot (or, after a failure, starts a fresh open).

**`WindowDefinition.ViewType` is validated by `WindowFactory`, and that is the only thing it is
for.** Written by every module and read by nobody, it would be worse than unused: it looks like a
declared invariant, so a wrong prefab address next to a right `ViewType` would read as checked when
nothing checks it. Validating at creation means the error can name the window type, the
expected view type *and* the address that produced the wrong prefab, with nothing on screen yet.

**A view's `CloseRequested` closes that view — whichever kind it is.** Don't add a branch where a
`Window` asking to close while a popup is on screen closes the **popup** instead. That is
back-button semantics arriving through the wrong door — `WindowView.CloseRequested` is raised by
that window's own close button, and a button that closes something else is a bug however defensible
the intention. Such a branch is also close to unreachable, since a modal popup's backdrop covers the
window beneath it. A real back button belongs in an input handler that asks the manager for the topmost
dismissible thing.

**`IWindowController` inherits `IDisposable`** rather than declaring a bare `Dispose()`.
`WindowsManager` always calls it, and a hand-declared method would mean `using` does not compile
against a controller, analysers cannot see that it owns something, and a container's disposable
handling walks straight past it — the shape would understate the contract.

**`ModalBackdropPresenter` is deliberately container-free.** It is constructed by `WindowsManager`
with `new`, instantiates its prefab with `Object.Instantiate`, and reads it with
`IUiPrefabProvider.GetLoaded` because `Refresh` runs inside synchronous open/close bookkeeping and
cannot await (the address is preloaded by `AppEntryPoint`). It also removes only the dismiss
listener it added last time rather than calling `RemoveAllListeners`, which would wipe anything
wired on the prefab in the inspector — the engine quietly overruling the asset.

**`WindowsManager` calls `RefreshBackdrops` before `UILayerSorter`, never after.**
`ShowBackdropBehind` reorders the layer (backdrop, then the top window, both to last sibling), and
the sorter turns the resulting sibling order into `sortingOrder`. Applying the same layer twice is
idempotent and cheaper than tracking which layers are distinct.

## Extension points

- **New transition style:** implement `IWindowTransition`, reference it from a `WindowDefinition`.
  Nothing else changes.
- **New layer** (e.g. a `Toasts` layer): add a `UILayerType` value, a `Transform` on `UIRoot` with
  a `Canvas` (`overrideSorting`, its own sorting band) and a `GraphicRaycaster`, wire it on
  `UIRoot`, and point the relevant `WindowDefinition`s at it. The band is what `UILayerSorter`
  numbers that layer's children from, so pick one that does not overlap its neighbours.
- **New window type:** see `Docs/architecture.md` §6 — this whole subsystem is closed for modification for
  that workflow by design.

## Gotchas

- **An open that outlives the manager is released, not opened.** `OpenAsync` loads the prefab under
  `CancellationToken.None` (the provider may be sharing that load with other callers, so cancelling
  it is not the manager's call). If `Dispose()` runs while the load is in flight, the continuation
  sees `_isDisposed`, disposes the controller, hands the view back to the factory and ends the open
  as an `OperationCanceledException`; an `OpenAsync` after `Dispose()` does the same without loading
  anything. Cancellation rather than `ObjectDisposedException` because every caller — the queue
  runner in particular — already treats an ended lifetime as a cancel, and a quit should not log.

- `WindowHandle.SetState` ignores any state that is not later than the current one (the enum order
  is the lifecycle order). It does not reject a skipped stage, because stages are legitimately
  skipped (below). This matters in practice: a window closed while its controller is still
  initializing is already `Closing` when `InitializeAsync` returns. `OpenInstanceAsync` checks
  `IsClosing` right there and aborts as a cancellation, so the view (already back in the pool) is
  never shown; if anything did call `SetState(Opening)` afterwards, the ordering rule would ignore it
  instead of moving the handle backwards.
- Pooled views are **not destroyed**, so anything a view owns beyond what its controller resets on
  the next `Init` (a downloaded `Texture2D` is the current real example, in `OfferWindowView`)
  must be cleaned up in `ResetForPool()`, or a reused instance will flash stale content. A pooled
  view also comes back with its old sibling index and with `blocksRaycasts`/`interactable` off;
  `WindowFactory` (`SetAsLastSibling`) and `FadeScaleWindowTransition.PlayOpenAsync` reset those,
  and both lines are load-bearing — see
  [`pooling-and-ownership.md`](../knowledge-base/pooling-and-ownership.md) for the full list of
  what a reused view brings with it.
- `WindowLifecycleState` stages can be **skipped**: a window whose init or open transition is
  cancelled or throws goes straight from `Initializing`/`Opening` to `Closing` and then `Disposed`,
  never reaching `Active`. Test for the stage you care about; never infer its predecessor ran.
- The two close-ordering constraints in `CloseInstanceAsync` (stop counting the window as busy
  before `MarkClosed()`; raise `QueueBecameIdle` after it) both look removable and are not, and the
  second `CloseAsync` on the same handle must return a wait for the close in flight rather than a
  completed task. All three are written up in
  [`pooling-and-ownership.md`](../knowledge-base/pooling-and-ownership.md).
- `WindowFactory` has **no code-built fallback window** — every `WindowDefinition` must carry an
  address that resolves in the Addressables catalog, or `CreateAsync()` throws. This is
  intentional (prefab-only content, no code-generated UI), but it means a prefab that was moved
  out of `Assets/Content`, or lost its Addressable flag, is a hard failure rather than a degraded
  placeholder. A prefab that exists on disk but is not marked Addressable fails exactly the same
  way as one that was deleted — check the group before checking the file.
- `AddressablesUiPrefabProvider` **never releases a handle until `Dispose`**. That is right for
  six windows plus the backdrop and preloader and wrong for a real catalog; anything that grows
  this beyond a fixed, small set of always-needed prefabs needs a release policy first (see `Docs/architecture.md` §10).
- A new window prefab needs a `Canvas`, a `GraphicRaycaster` and a `CanvasGroup` on its root
  (`overrideSorting` can stay off in the prefab; `UILayerSorter` turns it on at open). Miss the raycaster and the window renders perfectly while ignoring
  every click; miss the `Canvas` and it renders underneath every window that has one. Copy an
  existing window prefab rather than building the root from scratch.
- Each layer allows 999 children before its sorting band collides with the next one. Pooled views
  stay parented to their layer while inactive and still occupy a slot, so the practical ceiling is
  "distinct windows ever opened in this layer", not "open at once". Nowhere near it today.
