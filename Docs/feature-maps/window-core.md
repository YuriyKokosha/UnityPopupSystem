# Feature map — Window core engine

**Folders:** `Assets/Scripts/UI/Core/**`, `UI/Definitions/**`, `UI/Enum/**`, `UI/Infrastructure/**`,
`UI/Runtime/**` (excluding nothing — this is the engine itself), `UI/Transitions/**`
**Depends on:** `Zenject` (DI container access in `WindowFactory`/module `CreateController`),
nothing from `Game`
**Depended on by:** `App` (wiring), every concrete window under `UI/Windows/**`,
`Game.Services.WindowQueue` (drives it, but only through `IWindowsManager`/`WindowHandle`)

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
WindowFactory.Create(request)  -- via IWindowRegistry.Get(type) for the WindowDefinition
        |                         via IWindowControllerResolver.Create(type) for the controller
        |                         pools/instantiates the WindowView from Resources
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
| `Definitions/WindowDefinition.cs` | Immutable per-type config: layer, modality, `IsModal`, view type, prefab path, `IWindowTransition`, `CloseOnBackdropClick`. |
| `Infrastructure/UIRoot.cs` | `MonoBehaviour` holding the four layer `Transform`s (`Windows`/`Popups`/`Notifications`/`System`); `GetLayer(UILayerType)` is the only way anything gets a parent transform. |
| `Runtime/IWindowModule.cs` | One `WindowDefinition` + `CreateController()` per window type; multi-bound in `AppInstaller`. This is *the* extensibility seam — see `CLAUDE.md` §6. |
| `Runtime/Registry/WindowRegistry.cs` | `Dictionary<WindowType, WindowDefinition>` built once from every bound `IWindowModule`. Fully generic. |
| `Runtime/ControllerResolver/WindowControllerResolver.cs` | Same pattern for `IWindowController` creation. Fully generic. |
| `Runtime/Factory/WindowFactory.cs` | Loads/caches prefabs from `Resources`, pools closed `WindowView`s per `WindowType` in a `Stack<WindowView>` instead of destroying them, re-parents/re-orders a reused view exactly like a fresh `Instantiate` would. **No code-built fallback** — a missing prefab is a hard `InvalidOperationException`, logged loudly. |
| `Runtime/Manager/WindowsManager.cs` | The orchestrator: three logical stacks (`_baseWindow` for `MainGame`, `_windowStack`, `_popupStack`), open/close sequencing, abort-on-cancel-during-open handling, delegates modality to `ModalBackdropPresenter`. `IsQueueIdle` (both stacks empty) is what `WindowQueueRunner` polls. |
| `Runtime/WindowHandle.cs` | The public handle callers hold: `CloseAsync()`, `WaitForCloseAsync()`, `State`/`StateChanged`, `IsClosed`. `SetState`/`MarkClosed` are `internal` — only `WindowsManager` (and test fakes, via `[InternalsVisibleTo]`) drive them. |
| `Runtime/WindowInstance.cs` | Internal bookkeeping record (`WindowsManager`'s own view of an open window) — not exposed to callers. |
| `Runtime/WindowView.cs` | Abstract `MonoBehaviour` base: `Show()`/`Hide()`, `PlayOpenAsync`/`PlayCloseAsync` (defer to the assigned `IWindowTransition`, if any), `RequestClose()` for concrete views to raise, `ResetForPool()` hook for pooled per-open state cleanup. |
| `Runtime/Controller/WindowController.cs` | Abstract generic base (`TData : IWindowData`, `TView : WindowView`) — casts the view, converts/validates the payload, exposes `View`/`Handle` to subclasses, delegates to `OnInitializeAsync(TData, ct)`. |
| `Runtime/Backdrop/ModalBackdropPresenter.cs` | Per-layer dimming/input-blocking scrim: finds the topmost active instance per layer, shows/hides/parents a pooled backdrop `GameObject` behind it, wires backdrop-tap-to-close only when `WindowDefinition.CloseOnBackdropClick` is true. |
| `Transitions/IWindowTransition.cs`, `FadeScaleWindowTransition.cs`, `SharedWindowTransitions.cs` | Pluggable open/close animation strategy. `FadeScaleWindowTransition` is a dependency-free per-frame `CanvasGroup` alpha + uniform-scale lerp; `SharedWindowTransitions.PopupDefault` is a shared stateless instance most windows reuse. |
| `Services/IRemoteImageLoader.cs`, `RemoteImageLoader.cs` | Downloads a `Texture2D` via `UnityWebRequestTexture`, resolving relative keys against a configurable base URL (see `CLAUDE.md` §7). Not window-specific, but lives here because it's a UI-layer concern (popups display images; `Game` doesn't). |

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

## Extension points

- **New transition style:** implement `IWindowTransition`, reference it from a `WindowDefinition`.
  Nothing else changes.
- **New layer** (e.g. a `Toasts` layer): add a `UILayerType` value, a `Transform` on `UIRoot`, and
  point the relevant `WindowDefinition`s at it.
- **New window type:** see `CLAUDE.md` §6 — this whole subsystem is closed for modification for
  that workflow by design.

## Gotchas

- `WindowLifecycleState` transitions are not defensively validated for *order* — `SetState` only
  no-ops on setting the *same* state twice. Callers (today, only `WindowsManager`) are trusted to
  drive it in the documented order. If a second caller ever starts driving state directly, add
  order validation here first.
- Pooled views are **not destroyed**, so anything a view owns beyond what its controller resets on
  the next `Init` (a downloaded `Texture2D` is the current real example, in `OfferWindowView`)
  must be cleaned up in `ResetForPool()`, or a reused instance will flash stale content.
- `WindowFactory` has **no code-built fallback window** — every `WindowDefinition` must reference
  a real prefab under `Resources/`, or `Create()` throws. This is intentional (prefab-only
  content, no code-generated UI), but it means a missing/renamed prefab is a hard failure, not a
  degraded placeholder.
