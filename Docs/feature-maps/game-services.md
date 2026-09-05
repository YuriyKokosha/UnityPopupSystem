# Feature map — Game domain & services (incl. the faked backend)

**Folders:** `Assets/Scripts/Game/Domain/**`, `Game/Services/**` (excluding
`Services/WindowQueue`, covered in `window-queue.md`)
**Depends on:** `PopupSystem.Contracts` only — nothing from `UI`, enforced by the assembly graph
(`CLAUDE.md` §3)
**Depended on by:** `App` (wiring/connect flow), `UI.Windows.*` controllers,
`Game.Services.WindowQueue.Aggregators`
**Related knowledge base:** [`time-and-cooldowns.md`](../knowledge-base/time-and-cooldowns.md) (the
clock in full: monotonic anchoring, suspension, anchoring on first use rather than in a
constructor), [`resilience.md`](../knowledge-base/resilience.md) (which failures degrade the content
rather than the feature), [`unitask-and-cancellation.md`](../knowledge-base/unitask-and-cancellation.md)
(why a claim or a purchase takes a token it is never given)

## Purpose

The UI-agnostic "business logic" layer: what a player's profile/inventory look like, what a daily
reward or offer currently is, and the one seam (`IRpcManager`) standing in for a real backend.
This layer is what "Data Injection: a clean, type-safe mechanism to pass data into a popup" and
"Sourcing & Data" ultimately produce data *from*, before the UI layer ever sees it.

## Domain models (`Game/Domain/**`) — plain, immutable data

| Type | Holds |
|---|---|
| `Core/PlayerProfile` | `PlayerId`, `DisplayName`, `Level`. |
| `Inventory/InventoryResource`, `InventorySnapshot` | A single `(ResourceId, Amount)` pair, and a read-only list of them. |
| `Offer/OfferData` | Economic terms of an offer: id, `Rewards`, `StartsAtUtc`/`EndsAtUtc`, `IsActiveAt(utcNow)`. |
| `Offer/OfferRemoteContent` | The *presentation* half of an offer (title/description/action text/banner URL), deliberately separate from `OfferData` — see its own XML doc: terms are known locally, copy/art are fetched. `BannerImageUrl` may legitimately be null. |
| `Rewards/RewardPopupData` | `Title` + `Rewards` — the resolved output of a claim/purchase, shown by `RewardPopupController`. |

## Feature services (`Game/Services/<Feature>/`)

| Type | Responsibility |
|---|---|
| `Profile/PlayerProfileManager` | Holds `CurrentProfile`; set once at connect time. |
| `Inventory/PlayerInventoryManager` | In-memory resource balances (`Dictionary<string,int>`), `SetInventory` (full replace, e.g. at connect), `AddResources` (delta, e.g. after a claim/purchase), `BalancesChanged` event for UI to refresh from. |
| `DailyReward/DailyRewardManager` | `IsRewardAvailable()` (time-gated by `_nextAvailableAtUtc`), `ClaimRewardAsync()` (simulated latency, then a 1-minute re-availability window and a fixed reward — see Prototype note below). Raises `AvailabilityChanged` when a claim makes it unavailable, and exposes `NextAvailableAtUtc` so the queue can sleep until it comes back. |
| `Offer/OfferManager` | `GetActiveOfferData()` (currently a single hardcoded active offer, filtered through `OfferData.IsActiveAt`), `NextActivityChangeUtc` (when its active window next opens or closes), `GetOfferContentAsync` (fetches presentation via `IRpcManager.RemoteConfig`, **falls back to a safe hardcoded `OfferRemoteContent`** on any non-cancellation failure so a flaky backend never yields a blank popup), `PurchaseOfferAsync` (validates the offer is still active, simulated latency, credits `PlayerInventoryManager`, returns `RewardPopupData`). |
| `Time/ITimeProvider`, `Time/ServerSyncedTimeProvider` | The one place that answers "what time is it" — see below. |

## Time (`Game/Services/Time/`)

Nothing in the game layer reads `DateTime.UtcNow`. Everything time-gated — queue cooldowns, daily
reward availability, the offer's active window — takes `ITimeProvider`, for two independent
reasons: the device clock is player-controlled (and cooldowns are a monetisation surface), and an
ambient clock makes cooldown expiry untestable without sleeping.

`ServerSyncedTimeProvider` anchors to the server's clock during `AppConnectServerState`
(`ICoreRpcApi.GetServerTimeUtcAsync`) and then advances with a monotonic `Stopwatch` rather than by
re-reading the wall clock, so changing the device clock mid-session moves nothing. It also exposes
`IsSynced` (is this server time, or the device time we had to settle for?) and `IsSuspectedStale`,
which `TimeResyncTicker` in the `App` layer acts on. It is not a security boundary, and in this
build `FakeCoreRpcApi` returns the device clock anyway — that is the point of the seam: a real
backend replaces one method and every cooldown in the game stops trusting the device.

The full picture — why a stopwatch rather than a stored offset, why a monotonic clock stopping
during suspension made that choice *incomplete*, how the wall clock is used as a hint and never as
the time, and why offers and rewards anchor on first use rather than in a constructor — is in
[`time-and-cooldowns.md`](../knowledge-base/time-and-cooldowns.md).

## The faked backend (`Game/Services/Rpc/**`)

`IRpcManager` exposes four sub-APIs, one per concern, each with its own interface + fake:

```
IRpcManager
 ├─ Core          (ICoreRpcApi / FakeCoreRpcApi)          -> PlayerProfile, server UTC time
 ├─ Inventory     (IInventoryRpcApi / FakeInventoryRpcApi) -> InventorySnapshot
 ├─ WindowQueue   (IWindowQueueRpcApi / FakeWindowQueueRpcApi) -> IReadOnlyList<WindowQueueInfo>
 └─ RemoteConfig  (IRemoteConfigApi / FakeRemoteConfigApi) -> OfferRemoteContent
```

`FakeRpcManager` composes the four fakes behind the single `IRpcManager` contract a real networked
implementation would also satisfy. Every fake simulates latency via `UniTask.Delay` (700 ms–1000 ms)
specifically so callers that depend on real elapsed time — the connect screen's progress bar, the
offer's "Loading…" state — are exercised for real even with no live server. `FakeWindowQueueRpcApi`
is also where the demo's queue *content* is defined: `DailyReward` at priority 100,
non-interruptible, 0s cooldown; `Offer` at priority 50, interruptible, 20s cooldown — the exact
"mix of priorities and sources" the submission guidelines ask the demo scene to showcase.

Swapping in a real backend: write one real class per sub-interface (or one real `IRpcManager` if a
single client wraps everything), rebind it in `AppInstaller`. No caller anywhere depends on the
`Fake*` types directly — they only ever see the interfaces.

## Decisions specific to this layer

**`GetActiveOfferData()` hands back the same cached `OfferData` instance** rather than rebuilding
one. That started as an allocation fix, back when the queue polled availability four times a
second. The runner is event-driven now, so that argument is much weaker — but the caching stays for
a reason that does not depend on call frequency: **`OfferData` is the offer's identity.**
`PurchaseOfferAsync` takes one back and re-checks its window against the clock, and a caller holding
an instance from an earlier call has to be looking at the same offer, not at an equal copy. A fresh
object per call would make reference comparison — and any per-offer state added later — quietly
wrong.

**The offer's active window is anchored once, not derived from "now" per call.** Both ends used to
be computed as `utcNow.AddDays(-1) .. utcNow.AddDays(7)`, which made `IsActiveAt(utcNow)`
unconditionally true: the "no active offer" branch was unreachable, `OfferWindowAggregator` could
never report the offer as unavailable, and the queue's whole availability path was never exercised.
The window belongs in remote config alongside the copy and banner that `GetOfferContentAsync`
already fetches; anchoring it once is the interim step that at least makes it behave like a real
window and genuinely expire.

**`DailyRewardManager.AvailabilityChanged` is raised when a claim makes the reward *unavailable*,
and deliberately not when it becomes available again.** That second transition happens because a
timestamp passed, with nothing running to notice it. Whoever cares reads `NextAvailableAtUtc` and
sleeps exactly that long — which is precisely the split `IWindowQueueAggregator` encodes as
`AvailabilityChanged` vs `NextAvailabilityChangeUtc`.

**`OfferWindowAggregator.AvailabilityChanged` is never raised today, and that is correct rather than
an omission** — an offer's availability changes only when its active window opens or closes, which
is a timestamp passing. The day offers arrive from live config (pulled by an operator mid-session,
say) is when that event starts firing, and nothing else would have to change.

**A claim or a purchase takes a `CancellationToken` it is deliberately never given.** The parameter
is there because that method is the seam a real backend replaces and an HTTP call needs one; the
call sites pass `CancellationToken.None` on purpose. If cancellation ever does arrive, the claim is
all-or-nothing: nothing is applied until the call itself has gone through. See
[`unitask-and-cancellation.md`](../knowledge-base/unitask-and-cancellation.md).

**Every fake's latency is real latency.** `UniTask.Delay` in each fake API is not decoration: it is
what exercises the connect screen's progress bar and the offer's loading state for real without a
live server.

## Extension points

- **New player-facing feature with its own state** (e.g. a battle pass): new domain model(s) under
  `Game/Domain/<Feature>/`, a manager under `Game/Services/<Feature>/`, bind it in `AppInstaller`.
  If it needs server data, add a sub-interface to `IRpcManager` (or a new manager-level dependency)
  rather than overloading an existing one.
- **A feature that the queue should show a window for** additionally needs to *announce* its
  availability changes, because the queue waits rather than polls. Two shapes, both already in the
  repo: something a player action changes (a claim) raises an event the aggregator forwards —
  `DailyRewardManager.AvailabilityChanged`; something a passing timestamp changes (an offer window
  opening) has no event to raise and instead reports *when* — `OfferManager.NextActivityChangeUtc`.
  A manager that changes availability silently will have its window appear up to a minute late.
- **New remote-sourced content:** follow `OfferRemoteContent`/`IRemoteConfigApi`'s split of
  "terms known locally" vs. "presentation fetched remotely" rather than fetching everything, if
  only part of the data is genuinely dynamic.

## Prototype notes (see also `CLAUDE.md` §10)

- `OfferManager.GetActiveOfferData()` and `DailyRewardManager`'s reward contents are hardcoded,
  not driven by any config — there is exactly one possible offer and one possible daily reward in
  this build.
- `DailyRewardManager`'s "claimed" cooldown is 1 minute (not a real day) — deliberately short so
  the demo scene can show the reward becoming available again without waiting.
- `PlayerInventoryManager`/`PlayerProfileManager` hold state only in memory; nothing is persisted
  across a relaunch.
- `OfferManager`'s active window is anchored to "whenever the app first asked" plus seven days,
  rather than coming from remote config next to the copy and the banner it already fetches. It is
  anchored lazily rather than in the constructor so that it lands on synced server time, but that
  is an interim measure, not the design.
- `ServerSyncedTimeProvider` is re-anchored by `TimeResyncTicker` (periodically, and faster on a
  suspected resume), which covers drift and suspension. What it does not cover is the general
  problem: no server validates a claim, so `ITimeProvider` closes the date-change exploit and
  nothing more.
