# Resilience: what degrades, what is contained, what is hard

`Docs/architecture.md` §8 states the rule — *errors degrade, they don't crash the loop*. This is the
site-by-site reasoning behind it. The shape is always the same question: **if this fails, what stops
working, and for how long?**

## Failures that must never end a loop

A loop that dies takes every *future* occurrence with it, and the symptom is silence rather than an
error. Three loops are guarded from the inside:

- **`WindowQueueRunner.MonitorIdleAsync`** — the `try/catch` lives *inside* the `while`, not around
  it. An exception escaping a single pass must not end idle monitoring for the rest of the session;
  that would silently stop every future popup (daily reward, offer, …) from ever appearing, with no
  symptom beyond a console log. The catch is followed by a 1-second wait before the next pass,
  whatever wakes the loop: without it, a failure that repeats synchronously turns "never end the
  loop" into "never yield the main thread".
- **`AppConnectServerState.EnterAsync`** — a real backend can legitimately fail on the very first
  thing the app does (no connectivity yet, cold DNS, a 5xx). Without the retry loop that failure
  propagates up through `AppStateManager` into `AppEntryPoint`'s fire-and-forget start and leaves
  the player staring at a frozen "Connecting…" spinner forever. The loop shows an error and a Retry
  button on the preloader and awaits the tap.
- **`TimeResyncTicker`** — a failed re-sync is caught and the "in flight" flag is cleared in a
  `finally`. The previous anchor is still the best estimate available and strictly better than the
  device clock, and without the `finally` one failure ends re-syncing for the whole session.

`AppMainGameState` applies the same rule across a boundary: a failure showing the very *first*
automatic popup is caught and logged so it cannot prevent `StartIdleMonitoring()` from running.
Letting it through would silently stop every future popup, not just that one.

## Failures contained to one item

`WindowQueueRunner` keeps two pieces of per-window state, and the difference between them is the
whole policy:

- **`_failures`** — a retry time per window. A window whose turn threw (bad payload, a bug in its
  own `Init`, a prefab that will not load), or whose aggregator threw from `IsAvailable`,
  `CreatePayload` or `NextAvailabilityChangeUtc`, is logged and left out until 2 s later, doubling
  per consecutive failure up to 60 s, on the injected clock; a successful open clears it. The retry
  time is independent of wakes on purpose. Don't replace it with a per-burst set cleared at the
  start of every burst: the real `WindowsManager` raises `QueueBecameIdle` when a load fails, so a
  permanently broken window would be retried as fast as its load kept failing. A single misbehaving
  window must not take the rest of the queue down with it: the burst falls through to the
  next-highest item, lower priorities included.
- **`_presentedSinceAvailable`** — outlives a burst deliberately. See
  [`../feature-maps/window-queue.md`](../feature-maps/window-queue.md) for the starvation and
  reopen-loop problems it solves.

A window that failed to open gets **neither** `MarkShown` **nor** a `_presentedSinceAvailable`
entry. Neither cooldown timing nor "it had its turn" should advance for a window that never
appeared — otherwise one failed open sends it quiet for a cooldown it never earned.

The same containment applies inside a window's own flow. A reward popup that fails to open over a
claim or a purchase is logged by `RewardPopupLauncher` and reported as `null`; the controller still
awaits the transaction it has already started and lets *that* outcome decide the button, so a UI
failure can neither strand a running transaction nor offer the same reward twice.

Similarly, a *misbehaving close transition* is logged and the instance still proceeds to disposal.
It must not prevent disposal, and it must not vanish silently either, or a broken close animation
leaves no trace anywhere.

## Failures that degrade the content, not the feature

- **Remote config unreachable** → `OfferManager` serves `FallbackContent`: generic-but-correct copy
  instead of a broken or empty popup. Crucially the offer stays *usable* — the terms are local, only
  the copy was remote.
- **Banner missing or unreachable** → `OfferWindowController` falls back to the no-banner layout.
  The copy and the buy button are still fully usable, just without the promo image.
  `OfferRemoteContent.BannerImageUrl` may be null or empty (the fallback content has none); callers
  must handle that rather than assume a URL.
- **A request that succeeded but returned a non-image body** (wrong content-type, corrupted file) is
  treated as a load failure, rather than silently handing the caller a null texture it was not
  expecting.
- **A missing banner must surface as a failure the caller can degrade on**, not as a null texture
  handed back as though it had worked. That is the difference between a fallback layout and a blank
  rectangle.
- **The inventory cache is unreadable** (corrupt JSON, wrong schema version, hash mismatch) →
  `InventorySyncService` logs it and sends the server *no* hash, which makes the server answer with
  its snapshot. The player loses nothing, because the file was only ever a cache of the server's
  copy ([`persistence.md`](persistence.md)).
- **A save fails** (disk full, permissions) → `InventoryManager` logs the exception and keeps
  going: the in-memory state is right, the next mutation rewrites the file, and the next connect
  reconciles with the server anyway. A save failure must never roll back a grant the server has
  already made.
- **The inventory is full** → `RewardGrantService.CanGrant` says so *before* the daily reward or
  the offer starts its (simulated) round-trip, so the button is disabled with an "Inventory full"
  label and no cooldown is burned and no purchase charged for a reward that cannot land. Should a
  grant still be refused after the round-trip, `Grant` throws before anything moves, so the failure
  is clean — nothing half-granted (see "A grant is one local transaction" below).
- **`PreloaderOverlayView`** guards every serialized reference. A prefab missing one degrades the
  way the rest of the view does, instead of throwing during boot from inside the container.

## Failures that are deliberately hard

Not everything should degrade. Three things fail loudly and immediately:

- **An unresolvable prefab address.** There is no code-built fallback window; see
  [`addressables-and-content.md`](addressables-and-content.md). A caller handed `null` fails later
  and further from the cause.
- **A view whose type does not match its `WindowDefinition.ViewType`.** Checked in
  `WindowFactory` before anything reaches the screen, so the error can name the window type, the
  expected type and the address that produced the wrong prefab.

- **A failed inventory sync at connect.** Unlike the offer's remote copy, this fails the connect
  and goes through the existing retry loop. The inventory is where every reward lands; playing on
  a state the server has not confirmed is a worse outcome than one more Retry.

And one thing that must not be abandoned at all: a claim or a purchase already in flight. See
[`unitask-and-cancellation.md`](unitask-and-cancellation.md) — a claim is all-or-nothing, and
"cooldown started, nothing granted" is the shape of bug report nobody can reproduce.

## Observers cannot break the operation they observe

`InventoryManager.Changed`, `WalletManager.BalancesChanged` and
`DailyRewardManager.AvailabilityChanged` are raised through `StateChangeNotifier` (the window
engine does the same for `WindowHandle.StateChanged` — see
[`../feature-maps/window-core.md`](../feature-maps/window-core.md)). Each subscriber runs on its
own; a throw is logged with `Debug.LogException` and the remaining subscribers still run. The
reason is not tidiness: before this, a throwing UI subscriber on `Changed` escaped into the
mutation that raised it. The items were already in the inventory, the save had not been scheduled,
the currencies of the same grant were never credited, and the caller saw a failure — so the player
was invited to repeat a claim that had in fact half-happened. An observer failure is a presentation
bug; it must never read as an operation failure.

Ordering backs this up. `InventoryManager` commits the state, schedules the save, and only then
notifies, so nothing an observer does or throws can lose a committed mutation.

## A grant is one local transaction

`RewardGrantService.Grant(reward, price)` runs in three steps: **check** everything (the items fit,
no balance would leave `0..MaxBalance`, the price is covered) and throw before anything moves;
**apply** items, currencies and price with both managers' notifications deferred; **notify**. The
deferral matters as much as the check: a synchronous subscriber is user code, and if it ran between
the balance check and the charge it could spend the same gold again. Don't grant and then call
`Spend` separately on the reasoning that "both run synchronously" — that holds only until something
subscribes. `DailyRewardManager` sets its cooldown before calling `Grant` and rolls
it back if `Grant` refuses, so observers already see the reward as claimed and a refused grant
leaves it claimable.

This is a *local* guarantee. There is no purchase or claim RPC in this build; a real backend has to
settle the same transaction server-side and reconcile the client with its answer
([`../feature-maps/game-services.md`](../feature-maps/game-services.md)).

## Two robustness properties the queue guards explicitly

Both are one line away from being removed by accident:

- A cancelled interrupt watcher also completes, returning `false`. Only a genuine interrupt counts;
  mistaking a cancelled watcher for "something higher priority arrived" would force-close whatever is
  on screen at shutdown.
- `IWindowsManager.HasOpenPopups` vetoes an interrupt outright. Never yank a window out from under a
  popup it opened itself: its own flow is mid-await (a purchase, a claim) and would come back to a
  `View` and a `Handle` that closing already tore down, and the popup would be left on screen with
  its parent gone. Letting that turn finish is the safe outcome — the higher-priority window is not
  lost, the next scan picks it up.
