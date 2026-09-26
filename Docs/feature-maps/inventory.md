# Feature map — Inventory (items, slots, storage, server sync)

**Folders:** `Assets/Scripts/Game/Domain/Inventory/**`, `Game/Domain/Wallet/**`, `Game/Domain/Rewards/**`,
`Game/Services/Inventory/**`, `Game/Services/Wallet/**`, `Game/Services/Rewards/**`,
`Game/Services/Rpc/{Inventory,Wallet}/**`, `UI/Windows/Inventory/**`
**Depends on:** `PopupSystem.Contracts` (the `Game` half), `PopupSystem.Game` (the `UI` half)
**Depended on by:** `App` (connect flow, bindings), `UI.Windows.*` controllers, `DailyRewardManager`,
`OfferManager`
**Related knowledge base:** [`persistence.md`](../knowledge-base/persistence.md) (the file format, atomic
writes, why the server is the source of truth), [`resilience.md`](../knowledge-base/resilience.md)

## Purpose

A slot-based inventory: a set of item stacks with a slot limit from config, cached locally, and sent to
the server by hash at connect (the fake server does not compare it — see "Connect flow"). Currencies (`gold`, `gems`, `energy`) are **not** inventory: they live
in the wallet and never occupy a slot. Rewards carry both halves in one `RewardBundle`.

## Two managers, one grant service

| Type | Holds | Notes |
|---|---|---|
| `Wallet/WalletManager` | `Dictionary<currencyId, int>` | The old `PlayerInventoryManager` under its real name. `SetWallet` at connect, `AddCurrencies`/`Spend`, `BalancesChanged`. Every change is validated as a whole before anything moves (net delta per currency in `long`; no balance below zero or above `MaxBalance` = `int.MaxValue`; a negative credit is rejected) — see [`game-services.md`](game-services.md). |
| `Inventory/InventoryManager` | `InventorySnapshot` + `InventoryConfig` | All rules live here; every mutation is atomic, is handed to `IInventoryStorage` (`ScheduleSave`) and only then raises `Changed` once. Subscribers are isolated: one that throws is logged and cannot fail the mutation or skip its save. |
| `Rewards/RewardGrantService` | — | `CanGrant(bundle)` / `Grant(bundle)` / `Grant(bundle, price)`: one local transaction. Validates first — items fit (`CanAdd`), balances stay in range and the price is covered — and throws (`InventoryFullException`, `InsufficientFundsException`, `BalanceLimitExceededException`) **before** anything moves; then applies items, currencies and price with both managers' notifications deferred; then notifies once per manager. |

`DailyRewardManager` and `OfferManager` call `CanGrant` before their (fake) server round-trip and `Grant`
after it, so a full inventory never burns a cooldown or a purchase. The window controllers read the same
answer through `DailyRewardManager.CanClaimIntoInventory` / `OfferManager.CanPurchaseIntoInventory` (both
delegate to `CanGrant`) to disable Claim/Buy with an "Inventory full" label and re-check on
`InventoryManager.Changed`.

## Domain model (`Game/Domain/Inventory/`, plus `RewardBundle` from `Game/Domain/Rewards/`)

| Type | Meaning |
|---|---|
| `ItemDefinition` | Config of one item type: `ItemId`, `DisplayName`, `IconAddress`, `MaxStack` (≥ 1; 1 = never stacks), `Category` (`ItemCategory`: `Equipment`, `Consumable`, `Material`, `Container`). |
| `ItemCatalog` | The loaded definitions. `Get(id)` throws on an unknown id — a silent `MaxStack = 1` would hide a config error. |
| `InventoryConfig` | `SlotLimit` (`0` = unlimited) + `Catalog`. Comes from `IRemoteConfigApi.GetInventoryConfigAsync`. `MaxUnitsPerItem` (1 000 000) is a domain constant, not config: the most units of one item across all its stacks. |
| `ItemStack` | One occupied slot: `StackId`, `ItemId`, `Count` (≥ 1; normally ≤ `MaxStack`, can exceed it after a config lowering — rule 5 below). |
| `InventorySnapshot` | `Stacks` (sorted by `StackId`, enforced in the constructor), `Revision`, `NextStackId`, `Hash`. |
| `InventoryHasher` | `Canonicalize` / `Compute`: the one definition of the hash — see "The hash" below. |
| `ItemAmount` | "How many of what" as an *input*; the packer decides the stack layout. |
| `RewardBundle` | `Currencies` + `Items`; either may be empty, never null. Replaces `IReadOnlyList<InventoryResource>` everywhere. |
| `InventoryOperationResult` | `Success` / `Reason` (`InventoryFailureReason`: `UnknownItem`, `NotEnoughSlots`, `NotEnoughItems`, `QuantityLimitExceeded`) / `SlotsRequired` / `SlotsFree`. No exception for "does not fit". |

`StackId` is a monotonic counter inside the snapshot. It gives a stack an identity the window can act on
("use from *this* cell", "discard *this* stack") and a deterministic order for the UI and the hash.

## Stacking rules (`StackPacker`, stateless)

1. Adding `N` of an item tops up existing partial stacks of that item first, then opens new stacks of
   `MaxStack` each. New slots needed = `ceil(remainder / MaxStack)`.
2. One `TryAdd(items)` sums the slot need over **every** line of the bundle before touching anything. A
   bundle of "sword + 15 potions" into two free slots is refused whole; the sword does not land alone.
3. Removing takes from the smallest stack of that item first so partial stacks do not accumulate; a stack
   that reaches 0 disappears.
4. `SlotLimit = 0` disables the check entirely. A limit lowered below the used count throws nothing away:
   `FreeSlots` reads 0 and adding is blocked until the player frees a slot.
5. A stack over its `MaxStack` (config lowered after it was filled) simply has no free room; it is never
   "negative room".
6. An add that would take one item past `InventoryConfig.MaxUnitsPerItem` in total (held + added, summed
   over every line of the bundle) is refused whole with `QuantityLimitExceeded`, even with no slot limit.
   `StackPacker` accumulates in `long` and saturates at `int.MaxValue` rather than wrapping; saturation is
   safe because every caller compares the result with a smaller bound.

## The hash — a contract with the backend

`InventoryHasher.Canonicalize` produces `revision;itemId:count;itemId:count;…` in `StackId` order;
`Hash` is SHA-256 of that UTF-8 string, lower-case hex. `Revision` grows by one per successful mutation and
is part of the hash. `SlotLimit` is config, not state, and is not hashed.

Test vectors (fixed in `InventoryHasherTests`; a backend has to reproduce them):

| Canonical string | SHA-256 |
|---|---|
| `0` (empty inventory) | `5feceb66ffc86f38d952786c6d696c79c2dbc239dd4e91b46729d73a27fb57e9` |
| `3;sword:1;potion:7` | `661f4d03000ebd300783237b3de8ac30c00b214620c11d548d15c0139d6fc813` |

## Connect flow (`InventorySyncService.SyncAsync`)

```
RemoteConfig.GetInventoryConfigAsync  →  InventoryManager.Initialize(config)
IInventoryStorage.LoadAsync(playerId) →  InventoryManager.Load(playerId, local ?? Empty)   (no write)
IInventoryRpcApi.SyncAsync(local?.Hash, local?.Revision ?? 0)
   IsValid            → keep the local snapshot
   Replace(snapshot)  → InventoryManager.ReplaceFromServer(snapshot)                      (writes)
```

The local cache is loaded *before* the network call so a slow or failed sync still leaves the last known
state behind the retry prompt. A corrupt file is logged and treated as "nothing stored" — a missing hash
always makes the server answer with a snapshot. A failed `SyncAsync` fails the connect and goes through the
existing retry loop: the inventory is where rewards land, and playing on an unconfirmed state is worse than
one more retry. `FakeInventoryRpcApi` compares nothing: it returns the starter kit (sword + 3 potions) for
an empty hash and `Valid` for any non-empty one. A real backend would recompute the hash from its own copy
and answer `Replace` on a mismatch; that path is exercised by `InventorySyncTests` against a fake RPC.

This lives in `Game` rather than in `AppConnectServerState` so `InventorySyncTests` can drive it on fakes.

## Storage (`IInventoryStorage` / `FileInventoryStorage`)

One JSON file per player at `<root>/inventory/<playerId>.json`, `root` injected (`persistentDataPath` in
`AppInstaller`, a scratch directory in tests). Writes go to `.tmp` and are swapped in (`File.Replace` /
`File.Move`). The file carries `schemaVersion` and its own `hash`; a version mismatch or a hash mismatch
throws on load rather than returning a half-trusted snapshot. Saves inside `InventoryManager` are serialised
through one loop (`ScheduleSave` → `SaveLoopAsync`): a mutation during a write marks it dirty and the loop
writes the latest snapshot once more, so two writes never race and the file always ends on the newest state
(pinned by an `InventoryManagerTests` case that holds a save in flight via `FakeInventoryStorage.HoldSaves`).
The save is scheduled before `Changed` is raised, so nothing an observer does or throws can lose it.
A failed save is logged and swallowed — the in-memory state is right and the next mutation rewrites the file.

## The window (`UI/Windows/Inventory/`)

`InventoryWindowModule` (`WindowType.Inventory`, modal, `closeOnBackdropClick`, prefab
`UI/Windows/InventoryWindow`). A cell shows the item's **icon** (104 × 104, loaded from
`ItemDefinition.IconAddress` through `RewardIcons`, all catalog icons preloaded once when the window
opens) with `x{count}` in its corner for stackable items; the name is drawn only when the icon did
not load. See [`ui-atlas.md`](ui-atlas.md), "Reward icons". `InventoryWindowView` lays a fixed grid out in code — `SlotLimit` cells (or
at least 12 without a limit), positions computed from `_columns`/`_cellSize`/`_cellSpacing`, no
`LayoutGroup` — over pooled `InventorySlotView` cells that are re-bound on every render and blanked in
`ResetForPool`. Use (shown on `Consumable` items only) spends one unit from the tapped stack (`TryRemoveFromStack`), Discard drops the stack
(`TryRemoveStack`). An item that left the catalog is drawn by id rather than crashing. Opened from the
`MainGame` button; not part of the priority queue.

**Prefab:** `Assets/Content/UI/Windows/InventoryWindow.prefab` (860 × 1148 panel, 3 × 4 cells of 236 × 216,
spacing 22 × 20 — three columns so two real buttons fit side by side) is *generated* by `Tools/UI Kit/Inventory/Build InventoryWindow prefab`
(`Assets/Editor/UiKit/UiKitInventoryWindowBuilder.cs`) from a copy of `SettingsWindow`, marked Addressable
at `UI/Windows/InventoryWindow` in the `UI` group by the same script. `Tools/UI Kit/Inventory/Add inventory
button to MainGameWindow` adds the chest button and the `"3 / 12"` counter to `MainGameWindow.prefab` and
wires `_inventoryButton` / `_inventoryText`. Re-run both after a kit change rather than editing the prefabs
by hand; `Tools/UI Kit/Inventory/Render inventory screens` writes the check renders (partial, empty, full,
landscape, main game, plus `DailyReward` in landscape as the scaler control) to `Claude outputs/UIKit/Renders/`.
In landscape the panel is clamped to the screen and the grid scrolls — see [`windows.md`](windows.md). The cells' `Use`/`Drop` are `btn_pill_small` (a 72-high sibling of
`btn_pill`, added to the generator for this) — `btn_pill` is fixed at 140 high and has no room in a cell, and
`chip_9s` squashes into an oval at that size.

## Tests

`StackPackerTests`, `InventoryManagerTests`, `InventoryHasherTests`, `InventorySyncTests`,
`RewardGrantServiceTests`, `WalletManagerTests`, `OfferManagerTests` and the extended `DailyRewardManagerTests` in EditMode
(fakes: `FakeInventoryStorage`, `FakeInventoryRpcManager`, `TestItems`, `TestGrants`, `ProductionCatalog`);
`FileInventoryStoragePlayModeTests` for the real file system, and `InMemoryInventoryStorage` for the
PlayMode window fixtures. `Tools/Tests/Run … tests (write results)`
starts a suite from the menu and `PlayModeResultProbe` writes the outcome to
`Claude outputs/TestResults/<mode>.txt`.

## Deliberately not here

Positional grid / drag-and-drop, split/merge as player actions, equipment slots, item effects on Use, any
server validation of a mutation (a real server would at most check the hash; the fake does not even do that), a per-item `Discard` confirmation.
