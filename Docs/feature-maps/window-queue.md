# Feature map — Window priority queue

**Folders:** `Assets/Scripts/Game/Domain/WindowQueue/**`, `Game/Services/WindowQueue/**`
**Depends on:** `PopupSystem.Contracts` only — `IWindowsManager`, `WindowHandle`, `IWindowData`,
`WindowType`. The queue depends on the presentation *port*, never on the window engine or on any
concrete window, and the assembly graph enforces it: `PopupSystem.Game` does not
reference `PopupSystem.UI`, so a `using PopupSystem.UI.…` here is a build error.
**Depended on by:** `App.States.AppMainGameState` (starts/stops the runner),
`App.States.AppConnectServerState` (feeds `WindowQueueManager.SetItems`), `AppInstaller` (bindings)
**Related knowledge base:** [`time-and-cooldowns.md`](../knowledge-base/time-and-cooldowns.md)
(what a cooldown means, why a config refresh must not reset one),
[`resilience.md`](../knowledge-base/resilience.md) (the two suppression sets, the guarded loop, the
two interrupt guards), [`unitask-and-cancellation.md`](../knowledge-base/unitask-and-cancellation.md)
(the wake-signal pattern)

## Purpose

Satisfies "Queue Logic: … priority-based sequencing where certain popups can interrupt or wait
for others" end to end. This is the subsystem the assessment's own evaluation criteria singles
out for test coverage, and it's the one part of the codebase with dedicated automated tests (see
`tests.md`).

## The pieces

| File | Responsibility |
|---|---|
| `Game/Domain/WindowQueue/WindowQueueInfo.cs` | Immutable config for one queueable window: `WindowType`, `Priority` (higher wins), `CooldownSeconds` (minimum gap between shows), `AllowInterrupt` (can a strictly-higher-priority item force-close it mid-show; defaults `true`). |
| `Game/Services/WindowQueue/WindowQueueManager.cs` | Pure in-memory state: the current `WindowQueueInfo` set (replaced wholesale by `SetItems`, fetched from `IRpcManager.WindowQueue` at connect time), `GetItemsSortedByPriority()` (descending priority, `WindowType` as tiebreaker), per-type `_lastShownAt` for `IsCooldownReady`/`MarkShown`, `TimeUntilCooldownReady` (feeds the runner's scheduled wake), `Contains`, and `ItemsChanged` (raised by `SetItems`). Reads the clock through an injected `ITimeProvider`, never `DateTime.UtcNow` — see `game-services.md` for why. No Unity/async dependency — pure logic, hence directly unit-testable, cooldown expiry included. |
| `Game/Services/WindowQueue/Aggregators/IWindowQueueAggregator.cs` | Bridges a `Game` feature into the queue: `WindowType`, `IsAvailable()` (should this window be offered right now?), `CreatePayload()` (what `IWindowData`, if any, to open it with), plus the two members that make the runner event-driven — `AvailabilityChanged` (raise it when the answer to `IsAvailable()` changes) and `NextAvailabilityChangeUtc` (when it will change on its own, if that is knowable; `null` if not). Multi-bound in `AppInstaller`, exactly like `IWindowModule`. |
| `.../Aggregators/DailyRewardWindowAggregator.cs` | `IsAvailable()` → `DailyRewardManager.IsRewardAvailable()`; no payload. |
| `.../Aggregators/OfferWindowAggregator.cs` | `IsAvailable()` → `OfferManager.GetActiveOfferData() != null`; no payload (the window fetches its own data on init). |
| `Game/Services/WindowQueue/WindowQueueRunner.cs` | The actual scheduler — see below. |

## `WindowQueueRunner`, in detail

Two independent entry points into the same guarded routine:

- `ShowAvailableWindowsAsync()` — run one burst synchronously-as-far-as-possible (used for the
  initial burst in `AppMainGameState`, and in tests).
- `StartIdleMonitoring()` / `StopIdleMonitoring()` — an internal loop (`MonitorIdleAsync`) that
  runs the same burst logic whenever `IWindowsManager.IsQueueIdle` is true, then **sleeps until
  something can have changed** rather than ticking.

Both funnel into `ShowAvailableWindowsInternalAsync`, guarded by an `_isProcessing` flag (a
concurrent call is a same-frame no-op, not a second burst) and two pieces of per-type state:
`_presentedSinceAvailable` (survives bursts — it is what stops a `CooldownSeconds == 0` item that
is still "available", e.g. an unclaimed daily reward the player deliberately closed, from reopening
forever) and `_failures` (a window whose turn threw is left out until its retry time — see
"Failures" below).

### Waking, not polling

The runner does not poll. A polling timer would run for the life of the session, allocate on every
tick (the scan sorts the item list) and turn every timing in the system into a range instead of a
moment. `WaitForNextScanAsync` returns when the first of these happens:

- an aggregator raises `AvailabilityChanged` — a reward was claimed, an offer was pulled;
- `IWindowsManager.QueueBecameIdle` — the screen is free again;
- `NextScheduledWakeMs()` elapses — the earliest moment anything changes on its own, taken as the
  minimum over every item's remaining cooldown (`WindowQueueManager.TimeUntilCooldownReady`),
  every aggregator's `NextAvailabilityChangeUtc` and every failed window's retry time;
- `FallbackHeartbeatMs` (60 s) elapses, when none of the above exists.

The wake signal is a `UniTaskCompletionSource` **plus a `_wakeRequested` flag**, and the flag is
the load-bearing half: a signal can arrive between two waits, with nobody parked on the source, and
dropping it would mean sleeping through a change that already happened. The next wait consumes the
flag and returns immediately instead.

The same mechanism drives the interrupt watcher, so logically only one waiter matters at a time: the
idle loop is suspended inside `ShowAvailableWindowsInternalAsync` while the watcher waits.

**But "matters" is not "exists", and this is the subtlest thing in the class.** A cancelled watcher
is resumed by the player loop on a *later frame*, so it can still be inside `WaitForNextScanAsync`
— in its `finally` — after the burst has moved on and the next waiter has already published its own
source. That is why the `finally` clears the shared fields **only if they are still this call's
own**. Clearing them unconditionally would let a stale waiter either null out the live waiter's
source (so `Wake()` never reaches it and it sleeps the full computed delay, up to
`FallbackHeartbeatMs`) or swallow a wake request that had already arrived for it. Both
surface as *"the queued window appears up to a minute late, sometimes"* — the worst shape a defect
can take, and the reason `WindowQueueRunnerTests` puts a 5-second bound on that path even though it
cannot go red on demand.

`StopIdleMonitoring` also calls `Wake()` after cancelling, so a parked loop observes the
cancellation immediately instead of sitting out a full heartbeat first. The interrupt watcher gets
the same treatment when it is cancelled.

The heartbeat is a safety net, not a schedule. Nothing is supposed to depend on it — but "nothing
depends on it" is a claim about code that will be extended by people who have not read this class,
and a queue that silently stops showing windows is a much worse failure than one wasted scan a
minute.

Per iteration: `TryGetNextWindow` picks the highest-priority item that passes `IsEligible`, opens it
via `IWindowsManager.OpenAsync`, then `WaitForCloseOrInterruptAsync`:

- If `AllowInterrupt == false`: just `await handle.WaitForCloseAsync()`.
- If `AllowInterrupt == true`: race that same wait against `WatchForHigherPriorityAsync`, which
  *waits* on the same wake signal as the idle loop and re-checks for a strictly-higher-priority
  eligible candidate each time it wakes. If the watcher wins, `handle.CloseAsync()` is called
  (force-close) and the loop treats this as "not a completed turn" — the item is **not** marked
  shown and **not** recorded as presented, so it is reconsidered on the very next iteration once
  the interrupting window closes.

Two guards on that path are easy to remove by accident. A watcher whose token is cancelled also
completes, returning `false`; only `winArgIndex == 1 && interruptWon` counts as a real interrupt,
or shutdown would force-close whatever is on screen. And `IWindowsManager.HasOpenPopups` vetoes the
interrupt outright: a window that opened a popup of its own is mid-await in its own flow and would
come back to a `View` and a `Handle` that closing already tore down.

`IsEligible` is the single eligibility predicate, deliberately shared by the scan and the watcher —
if those two ever drift, the watcher can force-close a window in favour of a candidate the next
scan then refuses to open, and the player is left staring at nothing. `ReArmPresentedWindows` owns
every state change and runs first in the same pass; `IsEligible` itself never mutates.

### Failures

Aggregators are extension points, so every call into one — `IsAvailable`, `CreatePayload`,
`NextAvailabilityChangeUtc` — is contained to its own window. A window whose aggregator throws, or
whose `OpenAsync` throws, is logged (`RecordFailure`) and left out of the scan, the watcher and the
wake schedule until a **retry time**: 2 s after the first failure, doubling per consecutive failure,
capped at 60 s. The time is measured on the injected `ITimeProvider` and is independent of wakes —
the real `WindowsManager` raises `QueueBecameIdle` when a prefab load fails, and without the backoff
that wake would retry the same broken window immediately (a per-burst "failed" set, cleared at the
start of each burst, would allow exactly that). A successful open clears the
record. The rest of the queue, lower priorities included, carries on meanwhile.

Two rules keep the backoff honest. **One guard for every path:** `TryIsAvailable` is the only way the
runner asks an aggregator for availability, and it refuses while the window waits to retry — the
scan, the re-arm pass over already-shown windows and the interrupt watcher all go through it, so a
wake can neither call the broken source again nor push its retry time out. **A new retry time counts
at once:** when `NextAvailabilityChangeUtc` throws, the retry it sets is folded into the wait being
computed in that same pass, so a source that recovers without raising anything is looked at when its
retry is due, not a heartbeat later.

A failed window deliberately gets neither `MarkShown` nor a `_presentedSinceAvailable` entry —
neither cooldown timing nor "it had its turn" should advance for a window that never appeared.

`MonitorIdleAsync` wraps each pass in its own `try/catch`, so an unexpected exception cannot end
idle monitoring; after such a failure it waits 1 s (`MonitorFailureBackoffMs`) before the next pass
whatever wakes it, so a failure that repeats synchronously cannot spin the main thread.

## Data flow (typical burst)

```
WindowQueueRunner.MonitorIdleAsync
  -> if IsQueueIdle: ShowAvailableWindowsInternalAsync
       -> TryGetNextWindow  (GetItemsSortedByPriority -> ReArmPresentedWindows -> IsEligible)
       -> IWindowsManager.OpenAsync(type, aggregator.CreatePayload())
       -> WaitForCloseOrInterruptAsync
            (AllowInterrupt=false) -> handle.WaitForCloseAsync()
            (AllowInterrupt=true)  -> WhenAny(naturalClose, higherPriorityWatcher)
       -> on natural close: MarkShown(item); _presentedSinceAvailable.Add(type)
       -> on interrupt win: handle.CloseAsync(); item stays eligible, loop continues
       -> loop while IsQueueIdle and something eligible remains
  -> WaitForNextScanAsync
       -> wakes on AvailabilityChanged / QueueBecameIdle / next scheduled change / 60s heartbeat
  -> repeat
```

## Extension points

- **New queueable feature:** add an `IWindowQueueAggregator`, bind it, and give its `WindowType` a
  `WindowQueueInfo` entry in whatever supplies the queue config (`FakeWindowQueueRpcApi` today).
  `WindowQueueManager`/`WindowQueueRunner` need no changes. **Raise `AvailabilityChanged` whenever
  the answer to `IsAvailable()` changes**, and report `NextAvailabilityChangeUtc` if the feature
  knows when it will change on its own. An aggregator that does neither still works — the fallback
  heartbeat eventually notices — but its window will appear up to a minute late, and that lateness
  is the one symptom that looks like a hang rather than a bug.
- **Different interrupt policy** (e.g. "only interrupt after N seconds shown", or a cooldown that
  varies by context): the natural place is `WindowQueueInfo`/`WatchForHigherPriorityAsync` — keep
  the policy data-driven per item rather than special-casing a `WindowType` inside the runner.

## What a config refresh does, and does not, reset

`SetItems` replaces the item set wholesale and then **prunes** rather than clears the state keyed by
window type. Two places, same rule, for two different reasons:

- `WindowQueueManager._lastShownAt` — cooldown timestamps for types the new config still contains
  are **kept**. Clearing them would let "reconnect to skip the cooldown" become a real exploit on a
  real monetisation surface the moment the config can be refreshed mid-session. See
  [`time-and-cooldowns.md`](../knowledge-base/time-and-cooldowns.md).
- `WindowQueueRunner._presentedSinceAvailable` — entries for surviving types are also **kept**: a
  config refresh is not a new occasion to show a window the player already dismissed, and clearing
  the set wholesale would resurface exactly the windows the suppression exists to prevent. Entries
  for types that dropped out are removed, or they would sit there forever suppressing a window that
  is not even queueable — and still be suppressing it if the backend brought it back.

`WindowQueueManager.ItemsChanged` is what tells the runner to do the second half; it then wakes,
because a refresh can have made something queueable that was not before. Without that wake, the
first automatic popup after a mid-session refresh waits out the fallback heartbeat.

## Gotchas

- Priority ties break on `WindowType`'s **enum value**, ascending — not on any notion of
  "fairness" or insertion order. Two features sharing a priority will always resolve the tie the
  same way every time.
- The runner is only as responsive as its aggregators are honest. Everything hangs off
  `AvailabilityChanged` / `NextAvailabilityChangeUtc`; a feature that changes availability silently
  is invisible to the queue until the 60-second heartbeat, and nothing in the type system says so.
- `WindowQueueRunner` is `IDisposable` and is bound as one in `AppInstaller`. It subscribes to every
  aggregator and to `IWindowsManager.QueueBecameIdle` in its constructor, so skipping the
  `IDisposable` binding for a new runner-like class would leak those subscriptions rather than fail
  loudly.
- `GetItemsSortedByPriority()` hands back a **cached array**, rebuilt on `SetItems` rather than
  sorted per call — the runner asks up to three times per wake (`NextScheduledWakeMs`,
  `TryGetNextWindow`, `HasHigherPriorityWindowReady`) and the answer can only change when the item
  set does. The *same instance* goes to every caller until then. Callers only ever read it; a
  defensive copy per call would put back the allocation the cache removes.
- `CooldownSeconds == 0` means "no cooldown-driven repeat", **not** "repeat immediately". Reading it
  the other way is what let a still-available window reopen on every idle tick with no way to
  dismiss it, and it is why `_presentedSinceAvailable` outlives a burst.
- `ReArmPresentedWindows` owns every state change and must run *first* in the same pass;
  `IsEligible` never mutates. The two things that count as a new occasion to show a window that
  already had its turn are its availability *dropping* (so coming back is a fresh event) and a
  *positive* cooldown elapsing (the config explicitly asking for a repeat).
