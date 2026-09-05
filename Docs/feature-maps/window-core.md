# Feature map — Window core engine

**Folders:** `Assets/Scripts/UI/Core/**`, `UI/Definitions/**`, `UI/Enum/**`, `UI/Infrastructure/**`,
`UI/Runtime/**` (excluding nothing — this is the engine itself), `UI/Transitions/**`, plus the
presentation port in `Assets/Scripts/Contracts/**` (`WindowType`, `WindowLifecycleState`,
`IWindowData`, `EmptyWindowData`, `IWindowsManager`, `WindowHandle` — assembly
`PopupSystem.Contracts`)
**Depends on:** `Zenject` (typed factories only — `PrefabFactory<WindowView>` in `WindowFactory`,
`IFactory<TController>` in each module; no class here injects `DiContainer`), `Addressables`,
`TextMeshPro`, `Contracts`; nothing from `Game` in the engine itself
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
        |                         via IWindowControllerResolver.Create(type) for the controller
        |                         pops a pooled WindowView, or awaits IUiPrefabProvider.LoadAsync
        |                         (Addressables) and instantiates via PrefabFactory<WindowView>
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
WindowHandle.SetState(Opening)      -> View.PlayOpenAsync(ct)  [defers to IWindowTransition]
        |
        v
WindowHandle.SetState(Active)
```

Closing (`WindowsManager.CloseInstanceAsync`) runs the mirror image with a *non-cancellable*
token (`Closing` must always reach `Disposed`): unsubscribe the close event, cancel the instance's
lifetime token, `SetState(Closing)`, `View.PlayCloseAsync(CancellationToken.None)`,
`Controller.Dispose()`, `WindowFactory.Release(instance)` (pool, don't destroy),
`Handle.MarkClosed()` → `SetState(Disposed)`, refresh backdrops.

## Key files

| File | Responsibility |
|---|---|
| `Enum/WindowType.cs` | The closed set of window identities. Adding a window = adding a value here. |
| `Enum/WindowLifecycleState.cs` | `None → Initializing → Opening → Active → Closing → Disposed`, enforced by `WindowHandle.SetState` (no-ops on repeat, no validation of *order* today — see Gotchas). |
| `Enum/UIEntryKind.cs` / `UILayerType.cs` | `Window` vs `Popup` (which stack it lives on) and which `UIRoot` layer transform it parents under. |
| `Core/IWindowData.cs`, `EmptyWindowData.cs`, `WindowRequest.cs` | The payload contract. `IWindowData` is a pure marker; `EmptyWindowData` is the shared "no payload" struct; `WindowRequest` bundles type+payload for the factory. |
| `Definitions/WindowDefinition.cs` | Immutable per-type config: layer, modality, `IsModal`, view type, `PrefabAddress` (an Addressables address, e.g. `UI/Windows/OfferWindow`), `IWindowTransition`, `CloseOnBackdropClick`. |
| `Runtime/Content/IUiPrefabProvider.cs`, `AddressablesUiPrefabProvider.cs` | The content seam. `LoadAsync(address, ct)` loads and caches an `AsyncOperationHandle<GameObject>` per address; `GetLoaded(address)` is the synchronous lookup for something already preloaded (the modal backdrop); `LoadBlocking(address)` is the one deliberate `WaitForCompletion` call, used only by `AppInstaller` for the preloader overlay, which by definition has to exist before anything can be awaited on screen. Implements `IDisposable` and releases every handle it took. |
| `Infrastructure/UIRoot.cs` | `MonoBehaviour` holding the four layer `Transform`s (`Windows`/`Popups`/`Notifications`/`System`); `GetLayer(UILayerType)` is the only way anything gets a parent transform. |
| `Runtime/IWindowModule.cs` | One `WindowDefinition` + `CreateController()` per window type; multi-bound in `AppInstaller`. This is *the* extensibility seam — see `CLAUDE.md` §6. |
| `Runtime/Registry/WindowRegistry.cs` | `Dictionary<WindowType, WindowDefinition>` built once from every bound `IWindowModule`. Fully generic. |
| `Runtime/ControllerResolver/WindowControllerResolver.cs` | Same pattern for `IWindowController` creation. Fully generic. |
| `Runtime/Factory/WindowFactory.cs` | `CreateAsync` — awaits `IUiPrefabProvider` for the prefab, instantiates it through Zenject's `PrefabFactory<WindowView>`, pools closed `WindowView`s per `WindowType` in a `Stack<WindowView>` instead of destroying them, re-parents/re-orders a reused view exactly like a fresh `Instantiate` would. **No code-built fallback** — an unconfigured or unresolvable address is a hard `InvalidOperationException`. |
| `Runtime/Manager/WindowsManager.cs` | The orchestrator: three logical stacks (`_baseWindow` for `MainGame`, `_windowStack`, `_popupStack`), open/close sequencing, abort-on-cancel-during-open handling, delegates modality to `ModalBackdropPresenter`. `IsQueueIdle` (both stacks empty) is what gates the queue, and `QueueBecameIdle` is the event it raises when that flips back to true — the queue listens rather than polls. |
| `Contracts/WindowHandle.cs` | The public handle callers hold: `CloseAsync()`, `WaitForCloseAsync()`, `State`/`StateChanged`, `IsClosed`. `SetState`/`MarkClosed` are `internal` to `PopupSystem.Contracts` — only `WindowsManager` (and the EditMode fake) drive them, via an `[InternalsVisibleTo]` that names those two assemblies. Lives in `Contracts` rather than `UI/Runtime` because the queue, which is in `Game`, holds handles. |
| `Runtime/WindowInstance.cs` | Internal bookkeeping record (`WindowsManager`'s own view of an open window) — not exposed to callers. |
| `Runtime/WindowView.cs` | Abstract `MonoBehaviour` base: `Show()`/`Hide()`, `PlayOpenAsync`/`PlayCloseAsync` (defer to the assigned `IWindowTransition`, if any), `RequestClose()` for concrete views to raise, `ResetForPool()` hook for pooled per-open state cleanup. |
| `Runtime/Controller/WindowController.cs` | Abstract generic base (`TData : IWindowData`, `TView : WindowView`) — casts the view, converts/validates the payload, exposes `View`/`Handle` to subclasses, delegates to `OnInitializeAsync(TData, ct)`. |
| `Runtime/Backdrop/ModalBackdropPresenter.cs` | Per-layer dimming/input-blocking scrim: finds the topmost active instance per layer, shows/hides/parents a pooled backdrop `GameObject` behind it, wires backdrop-tap-to-close only when `WindowDefinition.CloseOnBackdropClick` is true. |
| `Transitions/IWindowTransition.cs`, `FadeScaleWindowTransition.cs`, `SharedWindowTransitions.cs` | Pluggable open/close animation strategy. `FadeScaleWindowTransition` is a dependency-free per-frame `CanvasGroup` alpha + uniform-scale lerp; `SharedWindowTransitions.PopupDefault` is a shared stateless instance most windows reuse. |
| `Services/IRemoteImageLoader.cs`, `RemoteImageLoader.cs` | Downloads a `Texture2D` via `UnityWebRequestTexture`, resolving relative keys against a configurable base URL (see `CLAUDE.md` §7). Not window-specific, but lives here because it's a UI-layer concern (popups display images; `Game` doesn't). |

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

**`IsBaseScreen` is a flag, not a `WindowType` check.** Three behaviours follow from it, and all
three used to be spelled `type == WindowType.MainGame` inside `WindowsManager`: the window is
single-instance (opening it again hands back the handle of the one already up rather than stacking a
second copy behind it), it lives in its own slot rather than on the window stack, and it does not
count as "the screen is busy" for the queue — the whole point of a base screen is that the queue
runs on top of it. Making it a flag is what makes the README's extensibility claim literally true:
the engine no longer names a single concrete window. The base-screen slot is *overwritten* rather
than required to be empty, because it can only ever hold a closed instance at that point (a live one
was handed back above) — the old shape pushed a reopened base screen onto the window stack instead,
where it permanently counted as "busy".

**`WindowDefinition.ViewType` is validated by `WindowFactory`, and that is the only thing it is
for.** It used to be written by all five modules and read by nobody, which is worse than unused: it
looks like a declared invariant, so a wrong prefab address next to a right `ViewType` reads as
checked when nothing checks it. Validating at creation means the error can name the window type, the
expected view type *and* the address that produced the wrong prefab, with nothing on screen yet.

**A view's `CloseRequested` closes that view — whichever kind it is.** There used to be a middle
branch: a `Window` asking to close while any popup was on screen closed the **popup** instead. That
is back-button semantics arriving through the wrong door — `WindowView.CloseRequested` is raised by
that window's own close button, and a button that closes something else is a bug however defensible
the intention. It was also close to unreachable, since a modal popup's backdrop covers the window
beneath it. A real back button belongs in an input handler that asks the manager for the topmost
dismissible thing.

**`IWindowController` inherits `IDisposable`** rather than declaring a bare `Dispose()`. The method
was always there and always called by `WindowsManager`, but declaring it by hand meant `using` did
not compile against a controller, analysers could not see that it owns something, and a container's
disposable handling would walk straight past it. The shape was understating the contract.

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
- **New window type:** see `CLAUDE.md` §6 — this whole subsystem is closed for modification for
  that workflow by design.

## Gotchas

- `WindowLifecycleState` transitions are not defensively validated for *order* — `SetState` only
  no-ops on setting the *same* state twice. Callers (today, only `WindowsManager`) are trusted to
  drive it in the documented order. If a second caller ever starts driving state directly, add
  order validation here first.
- Pooled views are **not destroyed**, so anything a view owns beyond what its controller resets on
  the next `Init` (a downloaded `Texture2D` is the current real example, in `OfferWindowView`)
  must be cleaned up in `ResetForPool()`, or a reused instance will flash stale content. A pooled
  view also comes back with its old sibling index and with `blocksRaycasts`/`interactable` off —
  see [`pooling-and-ownership.md`](../knowledge-base/pooling-and-ownership.md) for the full list of
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
  seven windows and wrong for a real catalog; anything that grows this beyond a fixed, small set of
  always-needed prefabs needs a release policy first (see `CLAUDE.md` §10).
- A new window prefab needs a `Canvas` (`overrideSorting` on), a `GraphicRaycaster` and a
  `CanvasGroup` on its root. Miss the raycaster and the window renders perfectly while ignoring
  every click; miss the `Canvas` and it renders underneath every window that has one. Copy an
  existing window prefab rather than building the root from scratch.
- Each layer allows 999 children before its sorting band collides with the next one. Pooled views
  stay parented to their layer while inactive and still occupy a slot, so the practical ceiling is
  "distinct windows ever opened in this layer", not "open at once". Nowhere near it today.
