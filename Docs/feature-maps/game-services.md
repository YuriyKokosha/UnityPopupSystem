# Feature map — Game domain & services (incl. the faked backend)

**Folders:** `Assets/Scripts/Game/Domain/**`, `Game/Services/**` (excluding
`Services/WindowQueue`, covered in `window-queue.md`)
**Depends on:** `PopupSystem.Contracts` only — nothing from `UI`, enforced by the assembly graph
(`Docs/architecture.md` §3)
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
| `Wallet/CurrencyAmount` | A single `(CurrencyId, Amount)` pair — `gold`, `gems`, `energy`. Currencies never occupy an inventory slot. |
| `Wallet/WalletSnapshot` | `Balances` (a list of `CurrencyAmount`): what `IWalletRpcApi` returns and `WalletManager.SetWallet` takes. |
| `Inventory/**` | The slot inventory's model: `ItemDefinition`, `ItemCategory`, `ItemCatalog`, `InventoryConfig`, `ItemStack`, `InventorySnapshot` (with its hash), `InventoryHasher`, `ItemAmount`, `InventoryOperationResult` (+ `InventoryFailureReason`). Detailed in [`inventory.md`](inventory.md). |
| `WindowQueue/WindowQueueInfo` | One queue entry: `WindowType`, `Priority`, `CooldownSeconds`, `AllowInterrupt`. Detailed in [`window-queue.md`](window-queue.md). |
| `Rewards/RewardBundle` | `Currencies` + `Items`: what a claim or a purchase grants, both halves in one value so a grant is all-or-nothing. |
| `Offer/OfferData` | Economic terms of an offer: id, `Price` (a `CurrencyAmount`, null for a free offer — `IsFree`), `Reward`, `StartsAtUtc`/`EndsAtUtc`, `IsActiveAt(utcNow)`. |
| `Offer/OfferRemoteContent` | The *presentation* half of an offer (title/description/action text/banner URL), deliberately separate from `OfferData` — see its own XML doc: terms are known locally, copy/art are fetched. `BannerImageUrl` may legitimately be null. |
| `Rewards/RewardPopupData` | `Title` + a `RewardBundle` — the resolved output of a claim/purchase, shown by `RewardPopupController`. |

## Feature services (`Game/Services/<Feature>/`)

| Type | Responsibility |
|---|---|
| `Profile/PlayerProfileManager` | Holds `CurrentProfile`; set once at connect time. |
| `Wallet/WalletManager` | In-memory currency balances (`Dictionary<string,int>`), `SetWallet` (full replace at connect), `AddCurrencies` (credits), `CanAfford(price)` / `Spend(price)` (the debit), `BalancesChanged` for the UI. It holds currencies, not items — it is not an inventory. Every change goes through one internal `Exchange(credits, debit)` that computes the net delta per currency in `long` and validates it before anything moves: a negative credit is an `ArgumentOutOfRangeException`, a short balance an `InsufficientFundsException`, a balance past `MaxBalance` (`int.MaxValue`) a `BalanceLimitExceededException` — each changes nothing. `EnsureCanExchange` runs the same validation without mutating; both are `internal`, for `RewardGrantService`. `BalancesChanged` subscribers are isolated (`StateChangeNotifier`, below). |
| `Inventory/InventoryManager`, `StackPacker`, `InventorySyncService`, `FileInventoryStorage` | The slot inventory: atomic `TryAdd`/`TryRemove` with a slot limit and a per-item unit limit, one isolated `Changed` event per mutation (raised after the save is scheduled), serialised saves to a JSON cache, and the hash check at connect. See [`inventory.md`](inventory.md). |
| `Rewards/RewardGrantService` | `CanGrant(bundle)` / `Grant(bundle)` / `Grant(bundle, price)`: one local transaction. It validates everything first — the items fit (`InventoryManager.CanAdd`), no balance passes its limit and the price is covered (`WalletManager.EnsureCanExchange`) — and throws before anything moves if not; then applies items, currencies and price with both services' notifications deferred; then notifies. `DailyRewardManager` and `OfferManager` ask `CanGrant` before their simulated round-trip and call `Grant` after. |
| `StateChangeNotifier` (internal) | How `InventoryManager`, `WalletManager` and `DailyRewardManager` raise their change events: each subscriber is invoked on its own and a throw is logged (`Debug.LogException`), so an observer can neither interrupt nor fail the operation that notified it; nested `Defer()` scopes hold notifications back until the outermost one ends. See [`resilience.md`](../knowledge-base/resilience.md). |
| `DailyReward/DailyRewardManager` | `IsRewardAvailable()` (time-gated by `_nextAvailableAtUtc`), `TimeUntilAvailable` (the countdown the window shows; zero while available), `Reward` (the fixed bundle a claim grants), `CanClaimIntoInventory()` (`RewardGrantService.CanGrant` on that bundle), `ClaimRewardAsync()` (refuses with `InvalidOperationException` while the reward is on cooldown or another claim is still in flight — the service, not the window, guarantees one grant per interval; throws `InventoryFullException` up front if the items do not fit; simulated latency, then it sets the 1-minute cooldown and calls `Grant`, rolling the cooldown back if `Grant` refuses (the items stopped fitting during the delay) — so observers of the grant already see the reward as claimed, and a refused grant leaves it claimable; a fixed reward — see Prototype note below). Raises `AvailabilityChanged` (isolated) when a claim makes it unavailable, and exposes `NextAvailableAtUtc` so the queue can sleep until it comes back. |
| `Offer/OfferManager` | `GetActiveOfferData()` (currently a single hardcoded active offer, filtered through `OfferData.IsActiveAt`), `NextActivityChangeUtc` (when its active window next opens or closes), `GetOfferContentAsync` (fetches presentation via `IRpcManager.RemoteConfig`, **falls back to a safe hardcoded `OfferRemoteContent`** on any non-cancellation failure so a flaky backend never yields a blank popup), `CanPurchaseIntoInventory(offer)` (`RewardGrantService.CanGrant` on its reward), `CanAfford(offer)` (the wallet covers `Price`), `PurchaseOfferAsync` (refuses with `InvalidOperationException` while another purchase is still in flight; validates the offer is still active, checks `RewardGrantService.CanGrant` and `WalletManager.CanAfford` — a full inventory throws `InventoryFullException`, an empty wallet `InsufficientFundsException`, both before anything moves — simulated latency, **the same checks again** because the world may have moved during the round-trip, then `RewardGrantService.Grant(offer.Reward, offer.Price)` — reward and price as one local transaction, validated together and notified only after both are applied, so no observer runs between the check and the charge; returns `RewardPopupData`). |
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

`IRpcManager` exposes five sub-APIs, one per concern, each with its own interface + fake:

```
IRpcManager
 ├─ Core          (ICoreRpcApi / FakeCoreRpcApi)          -> PlayerProfile, server UTC time
 ├─ Wallet        (IWalletRpcApi / FakeWalletRpcApi)      -> currency balances
 ├─ Inventory     (IInventoryRpcApi / FakeInventoryRpcApi) -> SyncAsync(hash, revision): Valid | Replace(snapshot)
 ├─ WindowQueue   (IWindowQueueRpcApi / FakeWindowQueueRpcApi) -> IReadOnlyList<WindowQueueInfo>
 └─ RemoteConfig  (IRemoteConfigApi / FakeRemoteConfigApi) -> OfferRemoteContent, InventoryConfig
```

`FakeRpcManager` composes the five fakes behind the single `IRpcManager` contract a real networked
implementation would also satisfy. Every fake simulates latency via `UniTask.Delay` (100 ms for the
server-time call, 500 ms–1000 ms for the rest) specifically so callers that depend on real elapsed
time — the connect screen's progress bar, the offer's "Loading…" state — are exercised for real even with no live server. `FakeWindowQueueRpcApi`
is also where the demo's queue *content* is defined: `DailyReward` at priority 100,
non-interruptible, 0s cooldown; `Offer` at priority 50, interruptible, 20s cooldown — the exact
"mix of priorities and sources" the submission guidelines ask the demo scene to showcase.

Swapping in a real backend: write one real class per sub-interface (or one real `IRpcManager` if a
single client wraps everything), rebind it in `AppInstaller`. Game managers and controllers see only
the interfaces; the demo item ids they use live in `Domain/Inventory/DemoItemIds` (the
`FakeRemoteConfigApi` constants alias them). The `Fake*` types are referenced by the composition
root, which binds `FakeRpcManager`, and by tests.

What the swap does **not** change: there is no purchase or claim RPC. `PurchaseOfferAsync` and
`ClaimRewardAsync` run a local `UniTask.Delay` and then a local mutation, whatever `IRpcManager` is
bound to. Making them server-authoritative needs new sub-API methods for the purchase and the claim,
and reconciliation of the local wallet/inventory/cooldown with the server's answer — an intentional
limitation of this prototype ([architecture.md §10](../architecture.md)), not something rebinding
provides.

## Decisions specific to this layer

**`GetActiveOfferData()` hands back the same cached `OfferData` instance** rather than rebuilding
one. That started as an allocation fix, back when the queue polled availability four times a
second. The runner is event-driven now, so that argument is much weaker — but the caching stays for
a reason that does not depend on call frequency: **`OfferData` is the offer's identity.**
`PurchaseOfferAsync` takes one back and re-checks its window against the clock, and a caller holding
an instance from an earlier call has to be looking at the same offer, not at an equal copy. A fresh
object per call would make reference comparison — and any per-offer state added later — quietly
wrong.

**The offer's active window is anchored once, not derived from "now" per call.** Computing both
ends as `utcNow.AddDays(-1) .. utcNow.AddDays(7)` would make `IsActiveAt(utcNow)` unconditionally
true: the "no active offer" branch would be unreachable, `OfferWindowAggregator` could never report
the offer as unavailable, and the queue's whole availability path would go unexercised.
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
all-or-nothing: nothing is applied until the (simulated) call itself has gone through. See
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

## Prototype notes (see also `Docs/architecture.md` §10)

- `OfferManager.GetActiveOfferData()` and `DailyRewardManager`'s reward contents are hardcoded,
  not driven by any config — there is exactly one possible offer and one possible daily reward in
  this build.
- `DailyRewardManager`'s "claimed" cooldown is 1 minute (not a real day) — deliberately short so
  the demo scene can show the reward becoming available again without waiting.
- `WalletManager`/`PlayerProfileManager` hold state only in memory; only the slot inventory is
  persisted across a relaunch, and only as a cache of the server's copy
  ([`persistence.md`](../knowledge-base/persistence.md)).
- `OfferManager`'s active window is anchored to "whenever the app first asked" plus seven days,
  rather than coming from remote config next to the copy and the banner it already fetches. It is
  anchored lazily rather than in the constructor so that it lands on synced server time, but that
  is an interim measure, not the design.
- `ServerSyncedTimeProvider` is re-anchored by `TimeResyncTicker` (periodically, and faster on a
  suspected resume), which covers drift and suspension. What it does not cover is the general
  problem: no server validates a claim, so `ITimeProvider` closes the date-change exploit and
  nothing more.
