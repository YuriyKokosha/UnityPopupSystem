# Feature map — App (bootstrap & app-level state machine)

**Folders:** `Assets/Scripts/App/**`
**Depends on:** `Game.Services.*`, `UI.Preloader`, `UI.Runtime.Manager`, `UI.Enum`, `UI.Infrastructure`
**Depended on by:** nothing (this is the outermost layer; only Unity's own scene bootstrapping
calls into it, via `AppInstaller` being a `MonoInstaller` on a scene object)

## Purpose

Owns process start-up: composes the DI container, then drives a small, linear app-level state
machine from "just launched" to "server connected" to "playing", handling the one place a
first-run failure (no connectivity) can realistically happen.

## Key files

| File | Responsibility |
|---|---|
| `Bootstrap/AppInstaller.cs` | The single Zenject composition root (`MonoInstaller`). Binds every service, the entire window engine, every `IWindowModule`/`IWindowQueueAggregator`, and the app states. Also builds the preloader overlay instance from its prefab. |
| `Runtime/AppEntryPoint.cs` | `IInitializable` entry point Zenject calls once the container is built. Fire-and-forget-starts `AppStateManager.RunAsync()`, guarded by `_isStarted` against being invoked twice. |
| `Runtime/AppStateManager.cs` | Runs the three `IAppState`s strictly in order (`Init → ConnectServer → MainGame`), always calling `ExitAsync()` on the outgoing state before `EnterAsync()` on the next. |
| `States/IAppState.cs` | `EnterAsync()` / `ExitAsync()` — the whole state contract. |
| `States/AppInitState.cs` | Shows the preloader with an "Initializing…" message. No real work yet; a real cold-start (config load, SDK init) would go here. |
| `States/AppConnectServerState.cs` | Fetches profile, inventory, and window-queue config from `IRpcManager`, updating preloader progress at each step. **The one place with a retry loop** — see below. |
| `States/AppMainGameState.cs` | Hides the preloader, opens `MainGame`, runs the queue's initial burst (`WindowQueueRunner.ShowAvailableWindowsAsync`), then starts idle monitoring. On `ExitAsync()`, stops idle monitoring. |

## The connect-retry loop (why it exists)

`AppConnectServerState.EnterAsync()` wraps its three RPC calls in a `while (true)` /
`try/catch (Exception)` that, on failure, shows an error + Retry button on the preloader and
`await`s a `UniTaskCompletionSource` that the button's click resolves, then loops back and retries
the whole connect sequence. Without this, a first-request failure (the most likely time for a
real backend to fail — no connectivity yet, cold DNS, etc.) would throw up through
`AppStateManager.RunAsync()` into `AppEntryPoint.Initialize()`'s fire-and-forget `async void`,
where it would simply vanish into Unity's log with the player stuck on a frozen loading screen
forever. This is the project's primary example of the "robustness / error handling" requirement
being satisfied outside the window engine itself.

## Extension points

- **New app-level phase** (e.g. an EULA/consent screen, an A/B config fetch): add an `IAppState`,
  bind it in `AppInstaller`, and splice it into `AppStateManager`'s fixed sequence. The sequence is
  intentionally linear and explicit rather than data-driven — for three states this is clearer
  than a generic state-list; revisit if the app grows a branching flow (e.g. skip connect when
  offline-capable).
- **New startup dependency** (e.g. a fourth RPC call before entering the main game): add it inside
  `AppConnectServerState.ConnectAsync()` alongside the existing three, in between the progress
  bar updates.

## Gotchas

- `AppEntryPoint.Initialize()` is `async void` by necessity (it implements Zenject's
  `IInitializable.Initialize()`, which is synchronous) — any exception that escapes
  `StartAsync()` after the point it starts truly executing asynchronously will not be observably
  caught anywhere. This is exactly why `AppConnectServerState` swallows-and-retries instead of
  letting connect failures propagate.
- `AppMainGameState.EnterAsync()` explicitly catches and logs (not rethrows) a failure from the
  *first* `ShowAvailableWindowsAsync()` burst specifically so a bad first popup can't prevent
  `StartIdleMonitoring()` from running — read the inline comment before "simplifying" this away.
