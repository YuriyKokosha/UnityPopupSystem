# PopupSystem — Advanced Unity Popup System

> ⚠️ **Project stage: PROTOTYPE.** This repository is a take-home technical assessment build. It
> demonstrates architecture, patterns, and reasoning, not a shippable feature. It has **not** been
> hardened for production: there is no real backend, Addressables are wired but there is no content
> pipeline behind them (one local group, no remote catalog, nothing released early),
> no persistence, no localization, and test coverage is scoped to the one subsystem the assessment
> asks to have it (the priority queue) rather than the whole codebase. See "Current limitations"
> below before building further on top of it, and see `CLAUDE.md` for the full technical brief.

## What this is

A generic Unity engine for opening, sequencing, and closing UI windows/popups — with
priority-based queueing, interrupts, cooldowns, a strict lifecycle, pooled views, remote-sourced
content, and modal backdrop handling — plus five concrete windows built on top of it as a working
demonstration:

| Window | Kind | Sourced | Queue-driven |
|---|---|---|---|
| `MainGame` | base screen | local | no (opened at startup) |
| `Settings` | window | local | no (opened by player action) |
| `DailyReward` | modal window | local | yes — priority 100, non-interruptible |
| `Offer` | modal window | **remote config + remote image** | yes — priority 50, interruptible |
| `RewardPopup` | modal popup | local, driven by an in-flight claim/purchase | no (opened by another window's flow) |

Open the demo scene (`Assets/Scenes/MainScene.unity`) and press Play: the app connects to a
simulated backend (with a retry UI if that "connection" fails), opens the main screen, and the
priority queue automatically offers `DailyReward` and, once it's on cooldown or unavailable,
`Offer` — showcasing both the mix of priorities and the mix of local/remote sources the assessment
asks the demo to cover, including one window interrupting the other.

## Why it's built this way

The brief asked for a system that manages many popups with different priorities, sources, and
lifecycles, stays extensible without touching its core, and holds up under async/error conditions.
The design choices below are direct answers to that:

- **A strict, observable lifecycle for every window** (`Initializing → Opening → Active → Closing
  → Disposed`, exposed on `WindowHandle`) instead of an ad hoc "is it active" bool, so any caller —
  the queue runner, a diagnostics overlay, another window's flow — can reason about exactly what
  stage a window is in and be notified when it changes.
- **Adding a window type never means editing the engine.** `WindowRegistry`,
  `WindowControllerResolver`, `WindowFactory`, and `WindowsManager` are all generic over
  `IWindowModule` bindings; a new window is one new View/Controller/Module triad plus one binding
  in the composition root (`AppInstaller`). The same pattern (`IWindowQueueAggregator`) makes the
  priority queue extensible the same way. This is the "developer experience" criterion made
  concrete — see `CLAUDE.md` §6 for the exact steps.
- **The queue is a scheduler, not just a sorted list.** `WindowQueueRunner` reconciles priority,
  per-type cooldowns, live availability, and an opt-in interrupt policy every tick — an
  interruptible window is force-closed and correctly re-queued (not lost, not double-shown) the
  moment something strictly higher-priority becomes available, while a non-interruptible one is
  provably immune to that. This is also the one subsystem with dedicated automated tests, because
  it's the one the assessment's evaluation criteria calls out by name.
- **Views are pooled, not destroyed**, and prefab lookups are cached — a window that opens and
  closes repeatedly over a session (which every queue-driven popup does) allocates a new
  GameObject exactly once, not on every open.
- **A single async idiom throughout (`UniTask` + `CancellationToken`)**, with loading states that
  are real (a window can open and *show* "loading" before its data resolves — see `Offer` and
  `RewardPopup`) rather than instant stubs, and with cancellation correctly unwinding when a window
  is dismissed mid-fetch.
- **Failure degrades locally, it never take down the whole system.** A remote-config fetch that
  fails falls back to safe generic copy; a single popup that throws while opening is logged and
  skipped for this cycle without stopping the queue's idle monitor; a failed initial server
  connect shows a Retry button instead of leaving the player on a frozen spinner forever. The
  specific failure mode each of these exists to prevent is written up in
  `Docs/knowledge-base/resilience.md`.
- **`Game` (domain + services) never depends on `UI`, and the compiler enforces it.** Each layer is
  its own assembly definition; `PopupSystem.Game` references only `PopupSystem.Contracts` — the
  presentation port (`WindowType`, `IWindowData`, `IWindowsManager`, `WindowHandle`) — so the
  queue/priority/business logic can be reasoned about and tested (see `Assets/Tests/EditMode/`)
  without a scene, a `Canvas`, or a DI container.
- **Local vs. remote sourcing is a first-class distinction, not an implementation detail.**
  `Offer` fetches its presentation copy and banner image from a config/image endpoint at display
  time (faked locally against `StreamingAssets` in this build), while its economic terms and every
  other window's content are compiled in — see `CLAUDE.md` §7 for exactly where that line is drawn
  and how it would move to a real CDN/CMS.

## Requirements coverage

| Assessment requirement | Where |
|---|---|
| Priority-based queue, interrupt-or-wait | `WindowQueueManager` + `WindowQueueRunner` (`Docs/feature-maps/window-queue.md`) |
| Strict lifecycle (Init/Opening/Active/Closing/Disposal) | `WindowLifecycleState` + `WindowHandle` + `WindowsManager` (`Docs/feature-maps/window-core.md`) |
| Local popups | `MainGame`/`Settings`/`DailyReward`/`RewardPopup` — prefabs under `Assets/Content/UI/Windows`, loaded by Addressables address |
| Remote popups (asset/content/config from an external source) | `Offer` — remote config content + remote-downloaded banner image |
| Type-safe data injection | `IWindowData` + generic `WindowController<TData, TView>` (`Docs/feature-maps/window-core.md`) |
| Transition system | `IWindowTransition` / `FadeScaleWindowTransition`, pluggable per window |
| Modal input blocking + backdrop | `ModalBackdropPresenter`, `CanvasGroup` raycast blocking during transitions |
| Extensible without touching the core | `IWindowModule` / `IWindowQueueAggregator` multi-bindings — see `CLAUDE.md` §6 |
| Async without blocking the UI | `UniTask` + `CancellationToken` throughout, real loading states |
| Memory/perf: pooling, cached lookups | `WindowFactory` view pooling, `IUiPrefabProvider` handle caching, texture cleanup on pool reset, an event-driven queue that sleeps instead of polling |
| Robustness: error handling, valid state under load | Retry loop on connect, per-popup try/catch in the queue runner, remote-config fallback content |
| Testing of the queue/priority system | `Assets/Tests/EditMode/` + `Assets/Tests/PlayMode/` (`Docs/feature-maps/tests.md`) |
| Demonstration scene with a mix of priorities/sources | `Assets/Scenes/MainScene.unity` |

## Project structure

```
Assets/
  Scripts/                                    one assembly definition per folder below
    Contracts/  the presentation port shared by Game and UI  -> PopupSystem.Contracts
    App/        bootstrap, app-level state machine (app.md)  -> PopupSystem.App
    Game/       domain models + services, incl. the faked RPC backend (game-services.md)
                                                             -> PopupSystem.Game
    UI/
      Core, Definitions, Infrastructure, Runtime, Transitions         <- the generic engine (window-core.md)
      Windows/<Feature>/                                              <- concrete windows (windows.md)
                                                             -> PopupSystem.UI
    Extensions/ small cross-layer utilities (e.g. UniTask.Share())
                                                             -> PopupSystem.Core
  Content/UI/     window content prefabs + the modal backdrop/preloader prefabs, Addressable group "UI"
  Scenes/         MainScene.unity — the demo
  Tests/EditMode/ the queue/priority system, against fakes (tests.md)
  Tests/PlayMode/ the window engine, against real prefabs on a real canvas (tests.md)
  Zenject/        vendored Extenject source, in its own Zenject/Zenject.Editor assemblies
CLAUDE.md         full technical brief for AI-assisted or new-engineer onboarding
Docs/feature-maps/*.md   one detailed map per module, linked from CLAUDE.md
```

## Tech stack

- Unity `6000.3.12f1` (Unity 6), Universal Render Pipeline, UGUI, TextMeshPro.
- [Zenject](https://github.com/modest-tree-games/zenject) for dependency injection (vendored
  under `Assets/Zenject`), composed in `AppInstaller`.
- [UniTask](https://github.com/Cysharp/UniTask) for all asynchronous code.
- Addressables for every UI prefab, behind an `IUiPrefabProvider` seam.
- Unity Test Framework (EditMode + PlayMode) + NUnit for the automated tests.

## Running it

1. Open the project in Unity `6000.3.12f1` (or newer within the same major version).
2. Open `Assets/Scenes/MainScene.unity`.
3. Press Play. The queue will automatically surface `DailyReward` and then, once it's on
   cooldown/claimed, `Offer`; use the main screen's Settings button and the daily-reward/offer
   claim & purchase buttons to exercise the on-demand `RewardPopup` flow.
4. To run the automated tests: **Window → General → Test Runner**, then Run All on the **EditMode**
   tab (44 tests — the queue, the daily reward and the server-synced clock) and the **PlayMode**
   tab (26 tests — the window engine, the concrete windows' own flows and the image loader). The
   EditMode tab lists 45: the extra one,
   `AddressableAssets.DocExampleCode.TestStub`, ships with the Addressables package and is not ours.

## Current limitations (prototype scope)

These are deliberate scope cuts for a take-home assessment, not oversights — see `CLAUDE.md` §10
for the full list and reasoning. In short: the entire backend is faked in-process
(`FakeRpcManager`); Addressables give the content pipeline its seam but not the pipeline itself
(one local group, no remote catalog, nothing released before shutdown); there is no persistence/save
layer (state resets on relaunch), no localization or analytics; only two features currently
participate in the priority queue; and
CI is written but has never run — the workflow sits in `Docs/ci/` and has to be moved into
`.github/workflows/` and given a Unity licence secret. Treat this as a solid foundation and a
demonstration of the intended patterns, not as a production client.
