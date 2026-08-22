# Feature map — Game domain & services (incl. the faked backend)

**Folders:** `Assets/Scripts/Game/Domain/**`, `Game/Services/**` (excluding
`Services/WindowQueue`, covered in `window-queue.md`)
**Depends on:** nothing from `UI` (see the one documented exception, `RewardPopupRequest`, noted
in `CLAUDE.md` §3 and `windows.md`)
**Depended on by:** `App` (wiring/connect flow), `UI.Windows.*` controllers,
`Game.Services.WindowQueue.Aggregators`

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
| `DailyReward/DailyRewardManager` | `IsRewardAvailable()` (time-gated by `_nextAvailableAtUtc`), `ClaimRewardAsync()` (simulated latency, then a 1-minute re-availability window and a fixed reward — see Prototype note below). |
| `Offer/OfferManager` | `GetActiveOfferData()` (currently a single hardcoded active offer, filtered through `OfferData.IsActiveAt`), `GetOfferContentAsync` (fetches presentation via `IRpcManager.RemoteConfig`, **falls back to a safe hardcoded `OfferRemoteContent`** on any non-cancellation failure so a flaky backend never yields a blank popup), `PurchaseOfferAsync` (validates the offer is still active, simulated latency, credits `PlayerInventoryManager`, returns `RewardPopupData`). |

## The faked backend (`Game/Services/Rpc/**`)

`IRpcManager` exposes four sub-APIs, one per concern, each with its own interface + fake:

```
IRpcManager
 ├─ Core          (ICoreRpcApi / FakeCoreRpcApi)          -> PlayerProfile
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

## Extension points

- **New player-facing feature with its own state** (e.g. a battle pass): new domain model(s) under
  `Game/Domain/<Feature>/`, a manager under `Game/Services/<Feature>/`, bind it in `AppInstaller`.
  If it needs server data, add a sub-interface to `IRpcManager` (or a new manager-level dependency)
  rather than overloading an existing one.
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
