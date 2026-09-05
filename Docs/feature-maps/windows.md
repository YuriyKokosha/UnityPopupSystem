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
Module (+ payload type, if it needs one) triad — see `CLAUDE.md` §6 for the step-by-step to add a
new one. This map covers the five that exist and the one cross-cutting utility they share.

## Two rules every one of these controllers follows

**Capture `View` and `Handle` before the first `await`.** `Dispose()` nulls both properties when the
window closes, and every line after an await may be running on a closed window. Without the
capture, a claim resolving after dismissal threw a `NullReferenceException` inside a `UniTaskVoid` —
no crash, no rollback, just a log line, and in one case from inside a `catch` block, which also
masked whatever made the operation fail in the first place. Then check `Handle.IsClosed` (or the
cancellation token) *before* writing anything into the view: the view you captured may already be a
pooled, reset instance, and writing into it puts the previous window's content behind the next
window's loading state.

**Do not `await` a content fetch inside `OnInitializeAsync`.** `WindowsManager` only shows the
window — `Show()` plus the open transition — once `InitializeAsync` has fully completed. Awaiting a
fetch there keeps the window hidden for the whole wait, so the loading state is never seen and the
window only ever appears already-loaded. Show a placeholder, return, and fill the view in from a
follow-up task.

## `MainGame` — the persistent base screen

- **Kind/layer/modal:** `Window` / `Windows` / not modal, and **no transition**: it is the permanent
  base screen, it should just be there rather than animate in.
- **`isBaseScreen: true`** — the one flag a normal window never sets. It is what makes the window
  single-instance, gives it its own slot instead of the window stack, and keeps it from counting as
  "the screen is busy" for the queue. See `window-core.md` for the three behaviours in detail; this
  used to be spelled `type == WindowType.MainGame` inside `WindowsManager`.
- **Controller:** shows the current `PlayerProfile` and a live-updating balances string (subscribes
  to `PlayerInventoryManager.BalancesChanged`, unsubscribes in `Dispose()`), and opens `Settings` on
  button click. `_isSettingsOpenRequested` guards against a double click opening two windows and is
  cleared only once the settings window has closed.

## `Settings` — minimal on-demand window

- **Kind/layer/modal:** `Window` / `Windows` / not modal, default popup transition.
- Simplest controller in the codebase (sets a status label, nothing else) — the reference example
  for "a window with no payload and no dependencies", and the one both PlayMode engine fixtures use
  for exactly that reason.

## `DailyReward` — local, non-interruptible, queue-driven

- **Kind/layer/modal:** `Window` / `Windows` / modal, **no** `closeOnBackdropClick`: an engagement
  popup with a primary action should close only via an explicit button, never an accidental outside
  tap.
- Offered by the priority queue via `DailyRewardWindowAggregator` (`IsAvailable()` →
  `DailyRewardManager.IsRewardAvailable()`), priority 100, `AllowInterrupt: false`, 0s cooldown in
  the demo config — nothing outranks or interrupts it.
- **Claim flow:** on click, disable the claim button, start `DailyRewardManager.ClaimRewardAsync()`
  and `.Share()` it, open `RewardPopup` with that shared task *before* it resolves so the popup's
  loading state is real, await the claim to restore the button on failure, then wait for the reward
  popup to close and close itself.
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
- **Init:** show a neutral placeholder immediately, then fetch title/description/CTA and the banner
  in the background (`OfferManager.GetOfferContentAsync`, which already degrades to safe fallback
  copy, so only cancellation can still throw here; then `IRemoteImageLoader` if a banner URL
  exists). A missing or unreachable banner falls back to the no-banner layout — the copy and the buy
  button are still fully usable.
- **Pooling caveat:** `OfferWindowView.ResetForPool()` **detaches** the banner texture without
  destroying it. `RemoteImageLoader` owns and caches what it hands out, so destroying it from a
  consumer would poison that cache with a destroyed entry. Clearing the reference is still required:
  a stale banner would flash under the loading indicator the next time the pooled instance is used.
  (This used to `Destroy` the texture, which was right while every download belonged to whoever
  asked for it.)
- **Purchase flow:** same `.Share()` + open-`RewardPopup`-before-resolving pattern, same
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
  title/rewards from `ObserveRewardAsync` once the task resolves — checking cancellation before
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

## Shared utility: `UniTaskShareExtensions.Share()`

Both `DailyReward`'s and `Offer`'s action flows need **two independent awaiters** of the same
in-flight operation: the window that started it (to restore its own button on failure) and
`RewardPopupController` (to drive its own loading state). `UniTask.Preserve()` does not support a
second *concurrent* await while the task is still pending. `Share()` does. Reach for it any time a
new window needs to open a follow-up popup against an operation it has not finished waiting on
itself; the mechanics are in
[`unitask-and-cancellation.md`](../knowledge-base/unitask-and-cancellation.md).

## The modules

All five are the same three lines of shape: build a `WindowDefinition`, implement
`CreateController()` through an injected `IFactory<TController>`, get multi-bound as `IWindowModule`
in `AppInstaller`. `WindowRegistry` and `WindowControllerResolver` are fully generic, so this is the
whole registration story — there is no central `switch` or dictionary to edit. The only per-window
decisions a module encodes are the ones listed above: kind, layer, modality,
`closeOnBackdropClick`, transition, `isBaseScreen`, and the prefab address.

## Building a demo/manual test around these

The demo scene wires exactly the mix the submission guidelines ask for: two sources (`DailyReward`
= fully local; `Offer` = remote config + remote image) at two priorities, one non-interruptible and
one interruptible, both queue-driven, plus two directly-opened windows (`Settings`, `RewardPopup`)
that never go through the queue. To manually exercise the interrupt path: let `Offer` open first
(lower priority, so it only opens when `DailyReward` isn't available/on cooldown), then wait for
`DailyReward` to become available again — it should force-close `Offer` and take over.
