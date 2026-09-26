# Feature map — Concrete windows

**Folders:** `Assets/Scripts/UI/Windows/**`, `Assets/Content/UI/Windows/**` (prefabs, Addressable in
the `UI` group), `Assets/Scripts/Extensions/UniTaskShareExtensions.cs`
**Depends on:** `UI` core engine (`window-core.md`), `Game.Services.*` (`game-services.md`)
**Depended on by:** nothing (these are leaf features — this is where you look for "how do I build
a window like the existing ones", not a dependency other code has on it)
**Related knowledge base:**
[`unitask-and-cancellation.md`](../knowledge-base/unitask-and-cancellation.md) (`Share()`, which
token a flow gets, cancelled-is-not-failed),
[`pooling-and-ownership.md`](../knowledge-base/pooling-and-ownership.md) (what a pooled view brings
back with it, who owns the banner texture),
[`resilience.md`](../knowledge-base/resilience.md) (what degrades when remote content fails)

Each window lives entirely in its own `UI/Windows/<Feature>/` folder as a View + Controller +
Module (+ payload type, if it needs one) triad — see `Docs/architecture.md` §6 for the step-by-step to add a
new one. This map covers the six that exist and the one cross-cutting utility they share.

## Two rules every one of these controllers follows

**Capture `View` and `Handle` before the first `await`.** `Dispose()` nulls both properties when the
window closes, and every line after an await may be running on a closed window. Without the
capture, a claim resolving after dismissal threw a `NullReferenceException` inside a `UniTaskVoid` —
no crash, no rollback, just a log line, and in one case from inside a `catch` block, which also
masked whatever made the operation fail in the first place. Then check `Handle.IsClosed` (or the
cancellation token) *before* writing anything into the view: the view you captured may already be a
pooled, reset instance, and writing into it puts the previous window's content behind the next
window's loading state.

**Do not `await` a remote content fetch inside `OnInitializeAsync`.** `WindowsManager` only shows the
window — `Show()` plus the open transition — once `InitializeAsync` has fully completed. Awaiting a
fetch there keeps the window hidden for the whole wait, so the loading state is never seen and the
window only ever appears already-loaded. Show a placeholder, return, and fill the view in from a
follow-up task. Local Addressable icon preloads (`RewardIcons.Preload*Async`) are the exception and are
awaited there on purpose, so the icons are on screen from the first frame.

## `MainGame` — the persistent base screen

- **Kind/layer/modal:** `Window` / `Windows` / not modal, and **no transition**: it is the permanent
  base screen, it should just be there rather than animate in.
- **`isBaseScreen: true`** — the one flag a normal window never sets. It is what makes the window
  single-instance, gives it its own slot instead of the window stack, and keeps it from counting as
  "the screen is busy" for the queue. See `window-core.md` for the three behaviours in detail; never
  spell them as `type == WindowType.MainGame` inside `WindowsManager`.
- **Controller:** shows the current `PlayerProfile`, the balances as a row of currency icons with
  their amounts (`RewardIcons.DescribeBalances`, gold → gems → energy, then anything else; subscribes
  to `WalletManager.BalancesChanged`; a currency nobody preloaded is drawn by id and switches to its
  icon once the sprite arrives) and the slot count under the chest button (subscribes to
  `InventoryManager.Changed`), unsubscribes from both in `Dispose()`, and opens `Settings` or
  `Inventory` on the corner buttons. `_isSettingsOpenRequested`/`_isInventoryOpenRequested` guard
  against a double click opening two windows and are cleared only once that window has closed.
- **Prefab:** the two round corner buttons sit on `Background` (the stage), not on the panel, as the
  mockup draws them. `InventoryButton` mirrors `SettingsButton` at `(48, -132)` anchored top-left,
  with `icon_chest` in gold light and a `"3 / 12"` label centred under it — the icon carries the word
  "inventory"; a longer label ran off the screen edge. Both are added by
  `Tools/UI Kit/Inventory/Add inventory button to MainGameWindow` rather than by hand.

## `Settings` — minimal on-demand window

- **Kind/layer/modal:** `Window` / `Windows` / not modal, default popup transition.
- Simplest controller in the codebase (sets a status label, nothing else) — the reference example
  for "a window with no payload and no dependencies", and the one `WindowsManagerPlayModeTests` uses
  for exactly that reason.

## `Inventory` — the slot grid, opened on demand

- **Kind/layer/modal:** `Window` / `Windows` / modal, **`closeOnBackdropClick: true`**: an
  information-and-housekeeping screen with no primary action, so an outside tap closing it is what
  the player expects. Not part of the priority queue.
- **Controller:** preloads every catalog icon, reads `InventoryManager.Stacks` and `Catalog`,
  maps every stack to a `SlotModel` (icon from `IconAddress`, name only as its fallback, `x{count}` only for stackables, `Use` only for consumables),
  renders `SlotLimit` cells (or at least 12 without a limit), and re-renders on
  `InventoryManager.Changed` — so a reward landing while the window is open shows up without a
  reopen. `Use` spends one unit from *that* stack (`TryRemoveFromStack`), `Drop` removes the stack
  (`TryRemoveStack`); the stack id, not the item id, is what a cell acts on. An item that has left
  the catalog is drawn by id rather than thrown on.
- **View:** a fixed grid laid out in code — `_columns` × `_cellSize` + `_cellSpacing`, no
  `LayoutGroup`, per the kit — over pooled `InventorySlotView` cells that are re-bound on every
  render and blanked in `ResetForPool()`. Each cell has an `Empty` and a `Filled` state toggled as
  whole objects, and its two small actions are `btn_pill_small` buttons (100 × 72): `btn_pill` is
  fixed at 140 high and has no room in a cell, and `chip_9s` collapses into an oval there because a
  40 px rect is shorter than the chip's 44 px borders.
- **Landscape.** With the scene's scaler (reference 1080 × 1920, match 0.5) a 1920 × 1080 screen is a
  1920 × 1080 canvas at scale 1.0, so the 1148-high design panel would run off both edges — the other
  windows are ≤ 960 and never hit this. The view clamps the panel to `parent height − 2 × 40` on
  `OnEnable`, on `OnRectTransformDimensionsChange` (orientation flips while open) and on `Render` (the
  Edit-Mode render check gets no `OnEnable`), and the grid lives in a `RectMask2D` + `ScrollRect`
  viewport that runs from the title block to the bottom inset: portrait shows all four rows and never
  scrolls, landscape shows three and scrolls the fourth. The viewport carries its own invisible
  `Image` (alpha 0, `cullTransparentMesh`) as the **drag surface**: a `ScrollRect` only hears a drag
  that starts on a raycast target, and everything in a cell except `Use`/`Drop` is
  `raycastTarget = false`, so without it the grid scrolled only when the finger landed on a button.
  `Inventory_GridScrolls_FromAnywhereInTheViewport_NotOnlyFromItsButtons` raycasts an empty card and
  the gap between two and fails on exactly that. The `ScrollRect` is **`Elastic` with inertia**
  (deceleration 0.135 — Unity's default and iOS's normal rate): it was `Clamped` at first, and with
  about one row of overflow a fling hit the end before any inertia could show. `ResetForPool` stops
  the movement and puts the grid back at the top, so a pooled window never reopens mid-fling. The explicit-size rule still holds — the design
  size is a serialized number, the clamp is the only thing that moves. The prefab is generated by
  `Tools/UI Kit/Inventory/Build InventoryWindow prefab` (`Assets/Editor/UiKit/UiKitInventoryWindowBuilder.cs`)
  from a copy of `SettingsWindow`, so the layout is numbers that can be re-run, and rendered by
  `Tools/UI Kit/Inventory/Render inventory screens`.
- The model, the rules and the storage behind it are in [`inventory.md`](inventory.md).

## `DailyReward` — local, non-interruptible, queue-driven

- **Kind/layer/modal:** `Window` / `Windows` / modal, **no** `closeOnBackdropClick`: an engagement
  popup with a primary action should close only via an explicit button, never an accidental outside
  tap.
- Offered by the priority queue via `DailyRewardWindowAggregator` (`IsAvailable()` →
  `DailyRewardManager.IsRewardAvailable()`), priority 100, `AllowInterrupt: false`, 0s cooldown in
  the demo config — nothing outranks or interrupts it.
- **Reward preview:** the card shows `DailyRewardManager.Reward` as icons — the bundle the claim
  actually grants. Don't put static reward text in the prefab: it drifts from what the claim grants.
- **Cooldown timer:** while `DailyRewardManager.IsRewardAvailable()` is false the claim button's
  slot shows a countdown instead (`CooldownTimer`: a frame-coloured `chip_9s`, Lilita in cream,
  `mm:ss`, or `hh:mm:ss` from an hour up, rounded up to whole seconds). The controller ticks it on a
  real-time `UniTask.Delay` aligned to the next whole second of `TimeUntilAvailable` - the remaining
  time itself comes from the server-anchored clock - under the window's lifetime token, and hands the
  slot back to the button the moment the reward is claimable. The cooldown wins over the inventory
  check: a full inventory only matters once there is something to claim. Through the queue the window
  normally opens only when the reward is available; the timer is what a window opened any other way,
  or a pooled one, shows instead of a button that would fail on tap.
- **Full inventory:** the controller asks `DailyRewardManager.CanClaimIntoInventory()` (i.e.
  `RewardGrantService.CanGrant` on the reward) on init and on every `InventoryManager.Changed`,
  and disables Claim with an "Inventory full" label when the answer is no — the manager would refuse the claim anyway, but a button that fails on tap is a
  worse experience than one that says why it is off.
- **Claim flow:** on click, disable the claim button, start `DailyRewardManager.ClaimRewardAsync()`
  and `.Share()` it, open `RewardPopup` with that shared task *before* it resolves so the popup's
  loading state is real, await the claim to restore the button on failure, then wait for the reward
  popup to close and close itself.
- **A popup that fails to open does not strand the claim.** `RewardPopupLauncher.TryOpenAsync`
  logs the failure and returns `null` instead of throwing, so the controller still awaits the claim
  it has already started: a claim that went through closes the window (the reward is granted, just
  without its popup), a claim that failed gives the button back. Awaiting `OpenAsync` outside the
  `try` would let a failed popup load leave `_isClaimInProgress` set and the button dead for the life
  of the window, with the claim's own outcome never observed. Resetting only the flag is not enough
  either: after a successful grant, the button must not come back at all.
- **The token is `CancellationToken.None`, deliberately.** A claim is a transaction; the player
  closing the window is a reason to stop *looking at* the outcome (the `IsClosed` checks), not to
  abandon it. The argument in full is in
  [`unitask-and-cancellation.md`](../knowledge-base/unitask-and-cancellation.md).
- **`OnInitializeAsync` restores a clean baseline every time**, rather than assuming a fresh view.
  The view may be a pooled instance whose claim button was disabled for the rest of its previous
  life and never re-enabled, because the window closed right after. A regression here is invisible
  on the first open and bricks every one after it.

## `Offer` — remote content + remote image, interruptible

- **Kind/layer/modal:** `Window` / `Windows` / modal, no `closeOnBackdropClick` (same reasoning as
  `DailyReward` — this is the monetisation popup).
- Offered by `OfferWindowAggregator` (`IsAvailable()` → `OfferManager.GetActiveOfferData() != null`),
  priority 50, `AllowInterrupt: true`, 20s cooldown in the demo config — it can be, and in the demo
  *is*, force-closed if `DailyReward` becomes available while it is showing.
- **Reward row:** the offer's `RewardBundle` is local, so its icons are on screen from the first
  frame, next to the "Loading offer..." placeholder; only copy and banner are remote. The remote copy
  does not list the contents ("500 gems, 50 energy and a sword") — the row says it.
- **Price:** the CTA carries the local price next to the remote verb — "Buy for 1 000 gold" — and is
  disabled with a reason ("Not enough gold", "Inventory full") when the player cannot complete the
  purchase; both `InventoryManager.Changed` and `WalletManager.BalancesChanged` re-evaluate it.
  While the copy is still in flight the button reads "Loading..." and is disabled — an empty,
  clickable button would sell the offer before the player had read it.
- **Init:** show a neutral placeholder immediately, then fetch title/description/CTA and the banner
  in the background (`OfferManager.GetOfferContentAsync`, which already degrades to safe fallback
  copy, so only cancellation can still throw here; then `IRemoteImageLoader` if a banner URL
  exists). A missing or unreachable banner falls back to the no-banner layout — the copy and the buy
  button are still fully usable.
- **Pooling caveat:** `OfferWindowView.ResetForPool()` **detaches** the banner texture without
  destroying it. `RemoteImageLoader` owns and caches what it hands out, so destroying it from a
  consumer would poison that cache with a destroyed entry. Clearing the reference is still required:
  a stale banner would flash under the loading indicator the next time the pooled instance is used.
- **Purchase flow:** same `.Share()` + open-`RewardPopup`-before-resolving pattern, same
  `RewardPopupLauncher` handling of a popup that fails to open, same
  `CancellationToken.None`, and here the stakes are plainer — this is the window the queue is
  *allowed* to force-close, so passing the lifetime token would let a higher-priority popup cancel a
  payment the backend may already have taken.
- **It closes itself *before* waiting out its popup** — the opposite order to `DailyReward`, and
  deliberately so. Once the purchase has gone through, the offer behind the reward popup is stale:
  it still shows "Buy" for something the player already owns. The daily reward window has nothing
  misleading left on it after a claim, so it stays until its popup is dismissed and then goes with
  it.

## `RewardPopup` — generic "here's your reward" popup

- **Kind/layer/modal:** `Popup` (not `Window` — lives on the popup stack/layer, stacks over a
  window), modal, **`closeOnBackdropClick: true`**: purely informational, so dismissing by tapping
  outside is expected and low-risk.
- Its payload, `RewardPopupRequest`, carries a `UniTask<RewardPopupData>` **that may still be
  pending**. The controller shows a loading state, returns immediately, and fills in
  title/rewards from `ObserveRewardAsync` once the task resolves — the reward as one icon per
  currency and item (`RewardIcons.PreloadAsync` then `Describe`), with `RewardLabel` kept only for the
  error sentence — checking cancellation before
  touching `View`, since the popup can be dismissed while the reward is still resolving. A caller
  that already has the final data can still use this by wrapping it in `UniTask.FromResult`.
- **Cancelled is not failed.** Telling the player *"couldn't claim your reward, please try
  again"* for an operation that was abandoned rather than rejected is a lie, and one that invites
  them to retry something that may well have gone through. The popup keeps its loading state and
  whoever cancelled the operation owns what happens to the window. `RewardPopupRequest` is
  documented as taking an in-flight operation, so the popup has to answer for this regardless of
  what today's two callers happen to pass — and they pass a token that cannot fire.
- **`RewardPopupRequest` lives here, not in `Game/Domain/Rewards`,** because it implements
  `IWindowData` — a UI-layer concept. Leaving it in the domain layer would make `Game` depend on
  `UI`, backwards from the dependency direction everywhere else. `RewardPopupData` (the resolved
  reward, with no UI dependency) correctly stays in `Game/Domain/Rewards`.
- **`RewardPopupLauncher`** sits next to it: the one helper both callers (`DailyReward`, `Offer`)
  use to open the popup over a running transaction, reporting a failed open as `null` rather than
  throwing so the caller keeps observing the transaction.

## Shared utility: `UniTaskShareExtensions.Share()`

Both `DailyReward`'s and `Offer`'s action flows need **two independent awaiters** of the same
in-flight operation: the window that started it (to restore its own button on failure) and
`RewardPopupController` (to drive its own loading state). `UniTask.Preserve()` does not support a
second *concurrent* await while the task is still pending. `Share()` does. Reach for it any time a
new window needs to open a follow-up popup against an operation it has not finished waiting on
itself; the mechanics are in
[`unitask-and-cancellation.md`](../knowledge-base/unitask-and-cancellation.md).

## The modules

All six are the same three lines of shape: build a `WindowDefinition`, implement
`CreateController()` through an injected `IFactory<TController>`, get multi-bound as `IWindowModule`
in `AppInstaller`. `WindowRegistry` and `WindowControllerResolver` are fully generic, so this is the
whole registration story — there is no central `switch` or dictionary to edit. The only per-window
decisions a module encodes are the ones listed above: kind, layer, modality,
`closeOnBackdropClick`, transition, `isBaseScreen`, and the prefab address.

## Building a demo/manual test around these

The demo scene wires exactly the mix the submission guidelines ask for: two sources (`DailyReward`
= fully local; `Offer` = remote config + remote image) at two priorities, one non-interruptible and
one interruptible, both queue-driven, plus three directly-opened windows (`Settings`, `Inventory`,
`RewardPopup`) that never go through the queue. To manually exercise the interrupt path: let
`Offer` open first (lower priority, so it only opens when `DailyReward` isn't available/on cooldown), then wait for
`DailyReward` to become available again — it should force-close `Offer` and take over.
