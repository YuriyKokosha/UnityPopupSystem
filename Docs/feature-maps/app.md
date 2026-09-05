# Feature map — App (bootstrap & app-level state machine)

**Folders:** `Assets/Scripts/App/**`
**Depends on:** `Game.Services.*`, `UI.Preloader`, `UI.Runtime.Manager`, `UI.Enum`, `UI.Infrastructure`
**Depended on by:** nothing (this is the outermost layer; only Unity's own scene bootstrapping
calls into it, via `AppInstaller` being a `MonoInstaller` on a scene object)
**Related knowledge base:** [`zenject-composition.md`](../knowledge-base/zenject-composition.md)
(binding shapes, disposal, why shutdown has to run), [`resilience.md`](../knowledge-base/resilience.md)
(the connect-retry loop and the two other guarded loops),
[`time-and-cooldowns.md`](../knowledge-base/time-and-cooldowns.md) (`TimeResyncTicker`),
[`addressables-and-content.md`](../knowledge-base/addressables-and-content.md) (the preload list and
the one blocking load)

## Purpose

Owns process start-up: composes the DI container, then drives a small, linear app-level state
machine from "just launched" to "server connected" to "playing", handling the one place a
first-run failure (no connectivity) can realistically happen.

## Key files

| File | Responsibility |
|---|---|
| `Bootstrap/AppInstaller.cs` | The single Zenject composition root (`MonoInstaller`). Binds every service, the entire window engine, every `IWindowModule`/`IWindowQueueAggregator`, and the app states. Also builds the preloader overlay instance from its prefab. |
| `Runtime/AppEntryPoint.cs` | `IInitializable` entry point Zenject calls once the container is built. Preloads the always-needed prefabs, then starts `AppStateManager.RunAsync()`, guarded by `_isStarted` against being invoked twice. |
| `Runtime/AppStateManager.cs` | Runs the three `IAppState`s strictly in order (`Init → ConnectServer → MainGame`), always calling `ExitAsync()` on the outgoing state before `EnterAsync()` on the next. Bound as `IDisposable` so the *last* state also gets torn down. |
| `Runtime/TimeResyncTicker.cs` | `ITickable` that re-anchors `ServerSyncedTimeProvider` periodically and, faster, whenever it reports `IsSuspectedStale`. |
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

## Decisions worth knowing before editing this layer

**Startup is loud, not silent.** `AppEntryPoint.Initialize()` is synchronous by Zenject's contract,
so the work behind it runs as a `UniTaskVoid` with a real try/catch: `OperationCanceledException`
is swallowed (that is Play Mode being left during the loading screen, i.e. a normal shutdown), and
anything else is logged as an error naming startup. It used to be `async void`, which made a missing
prefab or a broken binding throw into nobody. Booting is exactly where a failure has to be visible,
because everything diagnosed afterwards depends on knowing the app never finished starting.

**The connect state syncs the clock first, inside the retry loop.** Everything downstream — queue
cooldowns, the daily reward, the offer's active window — reads `ITimeProvider`, and until the sync
returns that falls back to the device clock. A client that failed to sync and carried on would be
running on player-controlled time with no sign that anything went wrong.

**The retry loop's `CancellationTokenSource` is scoped to the state.** The loop can wait
indefinitely — on a network call, or on the player noticing the Retry button — and the one thing
that has to be able to end that wait is the app going away. `ExitAsync` cancels it, and
`AppStateManager.Dispose` is what calls `ExitAsync` at shutdown. It used to be `while (true)` with
`default` handed to every call inside, so leaving Play Mode mid-connect left the loop running
against a container that no longer existed. The wait for a Retry tap needs the token most of all:
nothing else ever completes that source.

**Order matters in `AppInstaller`.** The binding shape (`concrete` → port → `IDisposable`, all via
`FromResolve`) is not stylistic; see
[`zenject-composition.md`](../knowledge-base/zenject-composition.md). `ServerSyncedTimeProvider` is
additionally bound as the concrete type because `AppConnectServerState` needs `SyncAsync()`, which
is deliberately absent from `ITimeProvider`.

**The preloader overlay comes from a prefab like everything else**, instantiated in the installer
the same way `WindowFactory` instantiates windows — nothing about it is built from code.

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

- `AppMainGameState.EnterAsync()` explicitly catches and logs (not rethrows) a failure from the
  *first* `ShowAvailableWindowsAsync()` burst specifically so a bad first popup can't prevent
  `StartIdleMonitoring()` from running. Letting it through would silently stop every future popup
  for the session, not just that one. Don't "simplify" this away.
- `AppStateManager` teardown from `Dispose` is **synchronous**. Every `ExitAsync` completes
  synchronously today (they cancel tokens and stop loops rather than awaiting), so nothing is lost —
  but a state that genuinely needs to await while shutting down will need a real async shutdown
  path, not this one.
- Nothing is re-synced before the first sync: `TimeResyncTicker` no-ops until the connect state has
  anchored the clock once.
