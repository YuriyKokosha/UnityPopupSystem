# Time, clocks and cooldowns

Nothing in `Game` reads `DateTime.UtcNow`. Anything time-gated takes `ITimeProvider`. The only
legitimate exceptions are inside the provider itself and in the fake backend that stands in for the
server.

## Two reasons, both load-bearing

1. **Correctness against the player.** Cooldowns, daily-reward availability and offer windows are
   monetisation surfaces. Reading the device clock means a player moves the clock forward and
   collects a "daily" reward as often as they like.
2. **Testability.** While the clock was ambient, cooldown expiry could not be tested at all: a test
   could only use a cooldown of 0 ("always ready") or 9999 ("never ready"), and anything in between
   would have needed a real sleep. With time injected, a test moves it forward by a fake minute.

Always UTC. The game layer has no business in local time, and mixing the two is how
off-by-one-timezone cooldown bugs happen.

## A monotonic stopwatch, not "device clock + stored offset"

`ServerSyncedTimeProvider` anchors to server time once at connect, then advances with a
`Stopwatch`:

```
UtcNow = IsSynced ? _serverUtcAtSync + _sinceSync.Elapsed : DateTime.UtcNow
```

An offset computed once is only correct until the player changes the device clock again, and
re-reading the wall clock is exactly the thing being defended against. A `Stopwatch` counts elapsed
real time from a monotonic source, so moving the device clock mid-session moves nothing.

Before the first successful `SyncAsync` it falls back to `DateTime.UtcNow`. That is a deliberate
choice: the alternative — time standing still at `DateTime.MinValue` until connect finishes — would
report every cooldown as expired, which is worse than the exploit the class exists to close.
`IsSynced` is exposed so a caller can tell "server time" from "device time we had to settle for";
a real client would surface that to analytics rather than silently trusting either.

`SyncAsync` is safe to call repeatedly and meant to be: re-anchoring replaces the previous anchor
outright rather than adjusting it, so a long session, a reconnect and a resume from background all
converge on the server's answer instead of accumulating error. The value is already one network trip
stale when it arrives; that is a sub-second error against cooldowns measured in minutes, so it is
not compensated for. A client needing better accuracy would halve the measured round trip and add
it.

## What this buys, and what it does not

It removes the trivial "set the date forward, collect the daily reward again" exploit — the one that
costs money. It is **not** a security boundary: a player who can attach a debugger or edit process
memory still lies, and the only real defence is the server refusing the claim. Grant/claim
validation belongs server-side regardless of what this class reports.

## A monotonic clock does not tick while the device sleeps

This is what made the stopwatch choice *incomplete* rather than wrong. `mach_absolute_time` on iOS
and `CLOCK_MONOTONIC` on Android both stop, so a session resumed after twenty minutes in a pocket
comes back with game time twenty minutes behind — and the player waits out a daily-reward cooldown
that never ticked.

`IsSuspectedStale` notices, by comparing the device's wall clock against the stopwatch since the
anchor. Two situations produce a divergence and both want a *fresh sync* rather than a correction:
the app was suspended, or the player moved the clock. The threshold is 5 seconds — generous on
purpose, since ordinary clock jitter and NTP nudges are well under a second while both real cases
are worth many seconds at least.

The wall clock is used **only as a hint that the anchor is worth refreshing**, never as the time
itself. So a player who moves the clock forward triggers a re-sync and gets the server's answer,
which is the opposite of what they were after. `_deviceUtcAtSync` exists for this comparison alone.

Acting on the hint is not the provider's job. `TimeResyncTicker` (App layer) re-syncs every five
minutes as drift control, and checks `IsSuspectedStale` every second so a resume is noticed within
a second. Both checks are a subtraction and a comparison; running them every frame would be noise in
a profile for no benefit. A failed re-sync must not be fatal and must not stop future ones — the
previous anchor is still strictly better than the device clock — and the "in flight" flag is reset
in a `finally`, or one failed re-sync silently ends re-syncing for the rest of the session.

Nothing is re-synced before the first sync: that one belongs to the connect state, which blocks
startup on it and retries until it succeeds. Time is synced **first**, before anything that gates
on it, and inside the retry loop deliberately — a client that failed to sync and carried on would be
running on player-controlled time with no sign that anything went wrong.

## Anchor on first use, not in a constructor

Constructors run while the Zenject container is being built, *before* the connect state has synced
the clock. A constructor-time anchor is therefore a device-clock anchor. Two consequences:

- `OfferManager` anchors the offer's active window on first access. Every caller reaches it through
  the queue, which only starts after connect, so by the time it matters the clock is real.
- `DailyRewardManager` starts `NextAvailableAtUtc` at `DateTime.MinValue` rather than "now" — the
  reward is available from the very first frame, and saying so this way means the constructor does
  not read a clock that has not been synced yet.

## What a cooldown means

`WindowQueueInfo.CooldownSeconds` is the **minimum gap between shows**, measured from the most
recent `MarkShown` — not from the first.

**`CooldownSeconds == 0` means "no cooldown-driven repeat", NOT "ready in 0s".** Reading it the
other way is what let a still-available window (an unclaimed daily reward the player deliberately
closed) reopen on every idle tick with no way out but claiming it. The comparison is `>=`, so a
cooldown is ready exactly at its boundary.

Two questions the queue asks, and both must agree: `IsCooldownReady` ("can it be shown now?") and
`TimeUntilCooldownReady` ("how long until it can?", `null` when ready now or when the cooldown is
zero). The runner folds the latter over every item to decide how long it may sleep. If they
disagreed at the boundary, the runner would sleep waiting for a cooldown the very next scan already
considers expired.

## A config refresh must not reset cooldowns

`WindowQueueManager.SetItems` **prunes** `_lastShownAt` — dropping entries for window types the new
config no longer contains, keeping them for the ones it still does.

Clearing the whole table is only invisible because the config arrives exactly once, at connect. The
moment it can be refreshed mid-session — a reconnect, a LiveOps push — clearing means every refresh
hands the player a fresh set of cooldowns, and "reconnect to skip the cooldown" is a real exploit on
a real monetisation surface. Keeping them is also the only reading that matches what a cooldown
means: *"this was shown at T"* is a fact about what happened, not about which config was loaded
when.

Types that dropped out are pruned rather than kept, because a type that is no longer queueable has
no cooldown to be on — and if the backend brings it back later, that is a new decision rather than a
continuation of the old one.

(A dictionary cannot be modified while its keys are being enumerated, hence the one reused scratch
list. This path is rare enough that one reused list is the whole cost of doing it correctly.)

## The fake backend reads the device clock, on purpose

`FakeCoreRpcApi.GetServerTimeUtcAsync` returns `DateTime.UtcNow`, which means the fake backend
cannot demonstrate the exploit the design exists to close. That is expected. The point of routing
time through this endpoint is that a real backend replaces this one method and every cooldown in
the game stops trusting the device, with no other code changing.
