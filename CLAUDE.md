# CLAUDE.md — PopupSystem

This file orients an AI agent (or a new engineer) working in this repository. It describes what
the project is, how it is put together, the conventions it follows, and where to look for detail
on any specific subsystem.

> **Project stage: prototype / take-home assessment.** This codebase was built to answer a Unity
> technical assessment (see `UnityDevTest.md`, if present, for the original brief). It is
> deliberately scoped to demonstrate architecture, not to ship. See "Prototype status & known
> gaps" near the end of this file before treating anything here as production-ready.

## 1. What this project is

A Unity "Popup System": a generic engine for opening, sequencing, and closing UI windows/popups
with priorities, cooldowns, interrupts, a strict lifecycle, pooled views, remote-sourced content,
and modal backdrop handling — plus five concrete windows (`MainGame`, `Settings`, `DailyReward`,
`Offer`, `RewardPopup`) built on top of it as a demonstration.

The demo scene is `Assets/Scenes/MainScene.unity`. Running it drives: app bootstrap → simulated
server connect (with retry-on-failure) → main game screen → an automatic queue that opens
`DailyReward` (local, high priority, non-interruptible) and `Offer` (remote-config-sourced content
+ remote image, lower priority, interruptible) according to priority/cooldown/availability, while
`Settings` and `RewardPopup` are opened on demand by user action.

## 2. Tech stack

- **Engine:** Unity `6000.3.12f1` (Unity 6), Universal Render Pipeline, UGUI (`com.unity.ugui`).
- **DI container:** [Zenject](https://github.com/modest-tree-games/zenject) — vendored as source
  under `Assets/Zenject` (not a package), bound in `AppInstaller`.
- **Async:** [UniTask](https://github.com/Cysharp/UniTask) (`com.cysharp.unitask` git package)
  everywhere instead of coroutines or bare `Task`. Every async API in the project returns
  `UniTask`/`UniTask<T>` and takes a `CancellationToken`.
- **Networking (demo):** `UnityWebRequest` via `RemoteImageLoader`, pointed at
  `Application.streamingAssetsPath` in this build (see §7).
- **Tests:** Unity Test Framework (`com.unity.test-framework`), EditMode, NUnit +
  `UniTask.ToCoroutine()` bridging for async test bodies. Located in `Assets/Tests/Editor`.

There is no backend, no build pipeline, and no CI configured — see §9.

## 3. Layering & dependency direction

The code is organized into three layers, and the dependency arrow only ever points one way:

```
App   (bootstrap, app-level state machine)
  |
  v
Game  (domain models + services — the "business logic", UI-agnostic)
  |
  v
UI    (window engine + concrete window Views/Controllers/Modules)
```

- **`Game` never references `UI`**, with one deliberate, documented exception:
  `RewardPopupRequest` (a UI payload type) is the only thing living outside `Game/Domain` even
  though its cousin `RewardPopupData` lives in `Game/Domain/Rewards` — see the type's own XML doc
  for the reasoning. If you're tempted to add a second exception, put the type under
  `UI/Windows/<Feature>` the same way, not under `Game/Domain`.
- **`App` wires `Game` and `UI` together** via `AppInstaller` (the single Zenject composition
  root) and drives the app-level state machine (`AppInitState` → `AppConnectServerState` →
  `AppMainGameState`).
- Everything is bound through interfaces (`IWindowsManager`, `IRpcManager`, `IWindowModule`,
  `IWindowQueueAggregator`, `IWindowTransition`, `IRemoteImageLoader`, …) and constructor-injected
  by Zenject. There is no `MonoBehaviour.Find`/singleton-locator pattern anywhere in the engine.

## 4. The window engine, in one paragraph

A window type is described once by a `WindowDefinition` (layer, modality, prefab path,
transition, whether backdrop-tap closes it) inside a small `IWindowModule` implementation that
also knows how to build that window's `IWindowController`. Every module is multi-bound in
`AppInstaller`; `WindowRegistry` and `WindowControllerResolver` are **fully generic** — they only
ever iterate the bound modules, so adding a new window type never means editing either of them.
`WindowFactory` instantiates (or pops from a pool) the view GameObject and hands back a
`WindowInstance`; `WindowsManager` owns the actual open/close orchestration, the three logical
stacks (base window, window stack, popup stack), and delegates backdrop compositing to
`ModalBackdropPresenter`. A window's controller only ever sees its own typed payload
(`WindowController<TData, TView>`) and drives a strict `WindowLifecycleState` machine
(`Initializing → Opening → Active → Closing → Disposed`) exposed on `WindowHandle`.

Full detail: `Docs/feature-maps/window-core.md`.

## 5. The priority queue, in one paragraph

`WindowQueueManager` holds the current set of queueable window types (priority + cooldown +
whether they can be interrupted), refreshed from `IRpcManager.WindowQueue` at connect time.
`WindowQueueRunner` is the thing that actually *acts* on it: while `IWindowsManager.IsQueueIdle`,
it asks each bound `IWindowQueueAggregator` (one per queueable feature — `DailyReward`, `Offer`)
whether its window is currently available, picks the highest-priority available+off-cooldown one,
opens it, and either waits for it to close naturally or — if it opted into
`AllowInterrupt` — races that wait against a watcher for a strictly-higher-priority window
becoming available, force-closing and re-queuing it if the watcher wins. A per-burst "already
shown" set prevents starvation of lower-priority items by a zero-cooldown one that's still
technically "available".

Full detail: `Docs/feature-maps/window-queue.md`.

## 6. Adding a new popup/window type (the intended workflow)

This is the extension point the assessment specifically asks for ("another developer [can] add
new popup types... without modifying the core engine"). Steps:

1. Create `Assets/Scripts/UI/Windows/<Feature>/` with three files following the existing windows
   as a template:
   - `<Feature>View.cs` — `sealed class ... : WindowView`, `[SerializeField]` references to its
     own prefab's UI parts, raises `RequestClose()` from a close button, exposes plain setter
     methods (`SetContent`, etc.) and any user-action events the controller subscribes to.
   - `<Feature>Controller.cs` — `sealed class ... : WindowController<TData, TView>`, injects
     whatever `Game` services it needs, implements `OnInitializeAsync` and (if it subscribed to
     anything) overrides `Dispose()` to unsubscribe.
   - `<Feature>Module.cs` — `sealed class ... : IWindowModule`, builds the `WindowDefinition`
     (pick a `WindowType`, `UIEntryKind`, `UILayerType`, modality, prefab resource path,
     transition, `closeOnBackdropClick`) and implements `CreateController()` via
     `_container.Instantiate<...Controller>()`.
   - If the window needs typed input, add an `IWindowData` implementation next to it (see
     `EmptyWindowData` for "no payload", `RewardPopupRequest` for "payload is an in-flight
     operation the window observes").
2. Add the new value to the `WindowType` enum (`Assets/Scripts/UI/Enum/WindowType.cs`).
3. Build the content prefab under `Assets/Resources/UI/Windows/<Feature>Window.prefab` (must live
   under `Resources/` — `WindowFactory` loads it via `Resources.Load`, there is no code-built
   fallback).
4. Bind the new `IWindowModule` in `AppInstaller.InstallBindings()`.
5. If this window should appear automatically via the priority queue (rather than only by a direct
   `IWindowsManager.OpenAsync` call from other UI), add an `IWindowQueueAggregator` for it under
   `Assets/Scripts/Game/Services/WindowQueue/Aggregators/` and bind it too; the backend's
   `WindowQueueInfo` list (currently `FakeWindowQueueRpcApi`) needs an entry with this window's
   priority/cooldown/`AllowInterrupt`.

Nothing else changes. `WindowRegistry`, `WindowControllerResolver`, `WindowFactory`,
`WindowsManager`, and `WindowQueueRunner` are all closed for modification for this workflow.

## 7. Local vs. remote sourcing

- **Local:** every window's *content prefab* is bundled in the app (`Assets/Resources/UI/...`) —
  there is no remote-fetched UI layout in this codebase, matching how live games typically ship
  screen layouts (LiveOps changes copy/config/assets, not the UI hierarchy itself).
  `MainGame`/`Settings`/`DailyReward`/`RewardPopup` also have fully local *content* (English copy
  compiled in, rewards computed client-side by `DailyRewardManager`/`OfferManager`).
- **Remote:** `Offer` demonstrates remote-sourced *content*: `IRemoteConfigApi` (faked by
  `FakeRemoteConfigApi`) supplies title/description/CTA copy plus a banner image reference at
  display time, and `IRemoteImageLoader`/`RemoteImageLoader` downloads that banner over
  `UnityWebRequest`. In this build the "remote" endpoint is `Application.streamingAssetsPath`
  (see `Assets/StreamingAssets/offers/starter-offer-banner.png` and the `WithArguments("file://" +
  ...)` binding in `AppInstaller`) — swapping in a real CDN/CMS means changing that one base URL,
  not any window/controller code.
- **Backend as a whole** is faked end-to-end: `IRpcManager` (`Core`/`Inventory`/`WindowQueue`/
  `RemoteConfig`) is satisfied by `FakeRpcManager`, whose four sub-fakes simulate latency via
  `UniTask.Delay` so loading states are exercised for real. Swapping in a live backend means
  writing one real implementation of `IRpcManager` (or of individual sub-interfaces) and changing
  one binding in `AppInstaller` — no caller depends on the fake types directly.

## 8. Conventions

- **DI:** constructor injection via Zenject everywhere; no field injection, no service-locator
  calls, no `FindObjectOfType`. The single composition root is `AppInstaller`.
- **Async:** `UniTask`/`UniTask<T>` + `CancellationToken`, never `async void` except at true
  fire-and-forget call sites (`.Forget()`), never a coroutine in new code.
- **Sealed by default:** concrete classes are `sealed` unless designed for inheritance
  (`WindowView`, `WindowController<TData, TView>` are the intentional exceptions).
- **Interfaces for everything crossing a layer boundary** (`I...Manager`, `I...Api`,
  `I...Aggregator`, `I...Module`, `I...Transition`, `I...Loader`) so the concrete implementation
  is swappable and mockable.
- **One feature, one folder:** a window's View/Controller/Module/payload type live together under
  `UI/Windows/<Feature>/`; a `Game` feature's manager + domain model live together under
  `Game/Services/<Feature>/` and `Game/Domain/<Feature>/`.
- **Extend via multi-binding, not switch statements:** `IWindowModule` and
  `IWindowQueueAggregator` are both Zenject multi-bindings consumed generically — this is the
  project's one recurring extensibility pattern and new features should follow it rather than add
  a branch to some central `switch`.
- **Errors degrade, they don't crash the loop:** a single window failing to open, a remote-config
  fetch failing, or a connect attempt failing must never take down `WindowQueueRunner`'s idle
  monitor or leave the player stuck — see the inline comments in `WindowQueueRunner`,
  `AppConnectServerState`, and `OfferManager` for the specific reasoning at each site. Preserve
  this property when touching those files.
- **Comments explain "why", not "what":** the codebase already leans on XML-doc and inline
  comments to record non-obvious reasoning (races, pooling gotchas, ordering requirements). Read
  them before changing the surrounding code, and keep adding them in the same style.

## 9. Testing

`Assets/Tests/Editor/` — EditMode, NUnit, no scene/Zenject container/real prefabs required for
these tests:

- `WindowQueueManagerTests.cs` — pure-logic coverage of priority ordering and cooldown gating.
- `WindowQueueRunnerTests.cs` — behavioral coverage of the runner (highest-priority selection,
  idle-gating, interrupt/force-close/reconsideration, non-interruptible immunity, re-entrancy
  guard) against `FakeWindowsManager`/`FakeWindowQueueAggregator`.

Run them from Unity's **Window → General → Test Runner → EditMode → Run All**. This is exactly
the area the assessment calls out for verification ("evidence that the core logic — especially
the queue and priority system — is verified and reliable"); if you touch `WindowQueueManager` or
`WindowQueueRunner`, extend these tests rather than only adding manual/scene testing. There is
currently no coverage for `WindowsManager`, `WindowFactory`, or any concrete window controller —
see §10.

## 10. Prototype status & known gaps

This is an assessment build, not a production client. Before building further on it, be aware of:

- **No real backend.** `FakeRpcManager` and its four sub-fakes are the entire "server" — there is
  no HTTP/RPC transport, auth, retry/backoff policy (beyond the one connect-retry loop in
  `AppConnectServerState`), or versioning story.
- **`Resources.Load`, not Addressables/AssetBundles.** Fine for five prefabs in a demo; will not
  scale to a real content pipeline (download-on-demand, memory budgets, content updates without a
  binary release) without migrating `WindowFactory`'s prefab loading.
- **Only two `IWindowQueueAggregator`s exist** (`DailyReward`, `Offer`); `RewardPopup` and
  `Settings` are opened directly via `IWindowsManager.OpenAsync`, not through the queue, and there
  is no aggregator/test coverage for a *third* concurrent queue source yet.
- **Single scene, no scene-load/unload story**, no save/persistence layer (all state is
  in-memory and resets on relaunch), no localization, no analytics/telemetry hooks.
- **No CI, no automated build pipeline**, no `.editorconfig`/analyzer ruleset checked in.
- **Test coverage is scoped to the queue/priority system** (per the assessment's own evaluation
  criteria) — `WindowsManager`, `WindowFactory`, and the concrete window controllers are exercised
  manually via the demo scene, not by automated tests.

None of these are bugs — they're the deliberate scope cut for a take-home assessment — but they
are exactly the things a "let's turn this into a real feature" pass should revisit first.

## 11. Feature maps

Detailed, per-module documentation lives under `Docs/feature-maps/`:

- [`app.md`](Docs/feature-maps/app.md) — bootstrap, app-level state machine, `AppInstaller`.
- [`window-core.md`](Docs/feature-maps/window-core.md) — the generic window engine (registry,
  resolver, factory/pooling, manager, handle/instance/view, backdrop, transitions).
- [`window-queue.md`](Docs/feature-maps/window-queue.md) — priority queue, cooldowns,
  interrupts, aggregators.
- [`game-services.md`](Docs/feature-maps/game-services.md) — domain models, feature managers,
  the faked RPC layer.
- [`windows.md`](Docs/feature-maps/windows.md) — the five concrete windows built on the engine.
- [`tests.md`](Docs/feature-maps/tests.md) — EditMode test strategy and how to extend it.
