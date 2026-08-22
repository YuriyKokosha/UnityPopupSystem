# Feature map — Window priority queue

**Folders:** `Assets/Scripts/Game/Domain/WindowQueue/**`, `Game/Services/WindowQueue/**`
**Depends on:** `UI.Runtime.Manager.IWindowsManager`, `UI.Runtime.WindowHandle`, `UI.Core.IWindowData`,
`UI.Enum.WindowType` (the queue layer is allowed to depend on the window-core *contracts*; it
never depends on any concrete window)
**Depended on by:** `App.States.AppMainGameState` (starts/stops it)

## Purpose

Satisfies "Queue Logic: … priority-based sequencing where certain popups can interrupt or wait
for others" end to end. This is the subsystem the assessment's own evaluation criteria singles
out for test coverage, and it's the one part of the codebase with dedicated automated tests (see
`tests.md`).

## The pieces

| File | Responsibility |
|---|---|
| `Game/Domain/WindowQueue/WindowQueueInfo.cs` | Immutable config for one queueable window: `WindowType`, `Priority` (higher wins), `CooldownSeconds` (minimum gap between shows), `AllowInterrupt` (can a strictly-higher-priority item force-close it mid-show; defaults `true`). |
| `Game/Services/WindowQueue/WindowQueueManager.cs` | Pure in-memory state: the current `WindowQueueInfo` set (replaced wholesale by `SetItems`, fetched from `IRpcManager.WindowQueue` at connect time), `GetItemsSortedByPriority()` (descending priority, `WindowType` as tiebreaker), per-type `_lastShownAt` for `IsCooldownReady`/`MarkShown`. No Unity/async dependency — pure logic, hence directly unit-testable. |
| `Game/Services/WindowQueue/Aggregators/IWindowQueueAggregator.cs` | Bridges a `Game` feature into the queue: `WindowType`, `IsAvailable()` (should this window be offered right now?), `CreatePayload()` (what `IWindowData`, if any, to open it with). Multi-bound in `AppInstaller`, exactly like `IWindowModule`. |
| `.../Aggregators/DailyRewardWindowAggregator.cs` | `IsAvailable()` → `DailyRewardManager.IsRewardAvailable()`; no payload. |
| `.../Aggregators/OfferWindowAggregator.cs` | `IsAvailable()` → `OfferManager.GetActiveOfferData() != null`; no payload (the window fetches its own data on init). |
| `Game/Services/WindowQueue/WindowQueueRunner.cs` | The actual scheduler — see below. |

## `WindowQueueRunner`, in detail

Two independent entry points into the same guarded routine:

- `ShowAvailableWindowsAsync()` — run one burst synchronously-as-far-as-possible (used for the
  initial burst in `AppMainGameState`, and in tests).
- `StartIdleMonitoring()` / `StopIdleMonitoring()` — an internal loop (`MonitorIdleAsync`, 500 ms
  tick) that calls the same burst logic whenever `IWindowsManager.IsQueueIdle` is true.

Both funnel into `ShowAvailableWindowsInternalAsync`, guarded by an `_isProcessing` flag (a
concurrent call is a same-frame no-op, not a second burst) and a per-burst `_shownThisBurst`
`HashSet<WindowType>` that prevents a `CooldownSeconds == 0` item that's still "available" (e.g.
an unclaimed daily reward) from re-winning the loop forever and starving everything below it.

Per iteration: `TryGetNextWindow` picks the highest-priority available+cooldown-ready+
not-yet-shown-this-burst item with a registered aggregator, opens it via
`IWindowsManager.OpenAsync`, then `WaitForCloseOrInterruptAsync`:

- If `AllowInterrupt == false`: just `await handle.WaitForCloseAsync()`.
- If `AllowInterrupt == true`: race that same wait against `WatchForHigherPriorityAsync` (polls
  every 250 ms for a strictly-higher-priority, available, cooldown-ready, not-yet-shown-this-burst
  candidate). If the watcher wins, `handle.CloseAsync()` is called (force-close) and the loop
  treats this as "not a completed turn" — the item is **not** marked shown and **not** added to
  `_shownThisBurst`, so it is reconsidered on the very next iteration once the interrupting window
  closes.

A window that throws while opening/waiting is logged, added to `_shownThisBurst` (so it doesn't
hot-loop for the rest of *this* burst) but deliberately **not** `MarkShown` (cooldown timing must
only advance for windows actually, successfully shown) — it gets another chance on the next idle
check.

## Data flow (typical burst)

```
WindowQueueRunner.MonitorIdleAsync (every 500ms while idle)
  -> ShowAvailableWindowsInternalAsync
       -> TryGetNextWindow  (WindowQueueManager.GetItemsSortedByPriority + per-item aggregator)
       -> IWindowsManager.OpenAsync(type, aggregator.CreatePayload())
       -> WaitForCloseOrInterruptAsync
            (AllowInterrupt=false) -> handle.WaitForCloseAsync()
            (AllowInterrupt=true)  -> WhenAny(naturalClose, higherPriorityWatcher)
       -> on natural close: WindowQueueManager.MarkShown(item)
       -> on interrupt win: handle.CloseAsync(); item stays eligible, loop continues
  -> loop while IsQueueIdle and something eligible remains
```

## Extension points

- **New queueable feature:** add an `IWindowQueueAggregator`, bind it, and give its `WindowType` a
  `WindowQueueInfo` entry in whatever supplies the queue config (`FakeWindowQueueRpcApi` today).
  `WindowQueueManager`/`WindowQueueRunner` need no changes.
- **Different interrupt policy** (e.g. "only interrupt after N seconds shown", or a cooldown that
  varies by context): the natural place is `WindowQueueInfo`/`WatchForHigherPriorityAsync` — keep
  the policy data-driven per item rather than special-casing a `WindowType` inside the runner.

## Gotchas

- `WindowQueueManager.SetItems` **replaces** cooldown tracking too (`_lastShownAt.Clear()`), not
  just the item set — re-fetching the queue config resets every cooldown, even for a type that was
  mid-cooldown. This is deliberate current behavior (and pinned down by
  `SetItems_CalledAgain_ResetsCooldownState`), but is easy to get surprised by if you assume
  `SetItems` is purely additive/config-only.
- Priority ties break on `WindowType`'s **enum value**, ascending — not on any notion of
  "fairness" or insertion order. Two features sharing a priority will always resolve the tie the
  same way every time.
- The interrupt watcher polls every 250 ms; it is not event-driven off aggregator availability.
  A feature whose availability needs to be reflected faster than that would need a push-based
  aggregator, which does not exist today.
