# Feature map — Concrete windows

**Folders:** `Assets/Scripts/UI/Windows/**`, `Assets/Resources/UI/Windows/**` (prefabs),
`Assets/Scripts/Extensions/UniTaskShareExtensions.cs`
**Depends on:** `UI` core engine (`window-core.md`), `Game.Services.*` (`game-services.md`)
**Depended on by:** nothing (these are leaf features — this is where you look for "how do I build
a window like the existing ones", not a dependency other code has on it)

Each window lives entirely in its own `UI/Windows/<Feature>/` folder as a View + Controller +
Module (+ payload type, if it needs one) triad — see `CLAUDE.md` §6 for the step-by-step to add a
new one. This map covers the five that exist and the one cross-cutting utility they share.

## `MainGame` — the persistent base screen

- **Kind/layer/modal:** `Window` / `Windows` / not modal, no transition (it's the permanent base,
  it shouldn't animate in).
- **Controller:** shows the current `PlayerProfile` and a live-updating balances string (subscribes
  to `PlayerInventoryManager.BalancesChanged`, unsubscribes in `Dispose()`), opens `Settings` on
  button click (guarded by `_isSettingsOpenRequested` against double-open from a double click).
- Special-cased in `WindowsManager.OpenAsync`: it is the one `WindowType` that becomes
  `_baseWindow` rather than going on `_windowStack`, and a second `OpenAsync(MainGame)` while one
  is already open just returns the existing handle instead of creating a second instance.

## `Settings` — minimal on-demand window

- **Kind/layer/modal:** `Window` / `Windows` / not modal, default popup transition.
- Simplest controller in the codebase (sets a status label, nothing else) — the reference example
  for "a window with no payload and no dependencies".

## `DailyReward` — local, non-interruptible, queue-driven

- **Kind/layer/modal:** `Window` / `Windows` / modal, no backdrop-tap-close (engagement popup —
  must be dismissed via its own button).
- Offered by the priority queue via `DailyRewardWindowAggregator` (`IsAvailable()` →
  `DailyRewardManager.IsRewardAvailable()`), priority 100, `AllowInterrupt: false` in the demo
  config — nothing outranks or interrupts it.
- **Claim flow:** on click, disables its own button, starts `DailyRewardManager.ClaimRewardAsync()`
  and `.Share()`s it (see below), opens `RewardPopup` with that shared task *before* it resolves so
  the reward popup's own loading state is real, awaits the claim to restore the button on failure,
  then waits for the reward popup to close and closes itself.

## `Offer` — remote content + remote image, interruptible

- **Kind/layer/modal:** `Window` / `Windows` / modal, no backdrop-tap-close (monetization popup).
- Offered by `OfferWindowAggregator` (`IsAvailable()` → `OfferManager.GetActiveOfferData() != null`),
  priority 50, `AllowInterrupt: true` and a 20s cooldown in the demo config — it can be, and in the
  demo *is*, force-closed if `DailyReward` becomes available while it's showing.
- **Init:** shows a neutral "Loading offer…" placeholder immediately (the window must already be
  visible before the fetch starts — see the controller's own comment on why this can't be
  awaited inside `OnInitializeAsync`), fetches title/description/CTA + banner asynchronously via
  `OfferManager.GetOfferContentAsync` (already degrades to fallback copy on failure) then, if a
  banner URL exists, via `IRemoteImageLoader`. A missing/failed banner falls back to a placeholder
  graphic without breaking the rest of the popup.
- **Pooling caveat:** `OfferWindowView.ResetForPool()` destroys and clears any downloaded banner
  `Texture2D` — the one piece of per-open state a fresh controller wouldn't otherwise overwrite —
  so a reused instance never briefly shows the *previous* offer's banner.
- **Purchase flow:** same `.Share()` + open-`RewardPopup`-before-resolving pattern as `DailyReward`.

## `RewardPopup` — generic "here's your reward" popup

- **Kind/layer/modal:** `Popup` (not `Window` — lives on the popup stack/layer, stacks over a
  window), modal, **`closeOnBackdropClick: true`** (purely informational, low-risk to dismiss).
- Its payload, `RewardPopupRequest`, carries a `UniTask<RewardPopupData>` **that may still be
  pending** — the controller shows a loading state immediately, then observes the task
  (`ObserveRewardAsync`) and fills in title/rewards (or an error message) once it resolves,
  checking `cancellationToken.IsCancellationRequested` before touching `View` in case the popup was
  dismissed while the task was still in flight.
- This is the one type living outside `Game/Domain` despite representing a "reward" concept —
  because it implements `IWindowData` (a UI concept), putting it in `Game/Domain` would create a
  `Game → UI` dependency, backwards from the rest of the codebase. `RewardPopupData` (no UI
  dependency, the actually-resolved value) correctly stays in `Game/Domain/Rewards`.

## Shared utility: `UniTaskShareExtensions.Share()`

Both `DailyReward` and `Offer`'s action flows need **two independent awaiters** of the same
in-flight operation: the window that started it (to restore its own button on failure) and
`RewardPopupController` (to drive its own loading state). `UniTask.Preserve()` does not support a
second *concurrent* await while the task is still pending (it throws "Already continuation
registered"); `Share()` (in `Assets/Scripts/Extensions/`, no UI dependency) awaits the source once
internally and republishes the outcome through a `UniTaskCompletionSource`, whose `OnCompleted`
genuinely supports multiple concurrently-registered continuations. Reach for this any time a new
window needs to open a follow-up popup against an operation it hasn't finished waiting on itself.

## Building a demo/manual test around these

The demo scene wires exactly the mix the submission guidelines ask for: two sources (`DailyReward`
= fully local; `Offer` = remote config + remote image) at two priorities, one non-interruptible and
one interruptible, both queue-driven, plus two directly-opened windows (`Settings`, `RewardPopup`)
that never go through the queue. To manually exercise the interrupt path: let `Offer` open first
(lower priority, so it only opens when `DailyReward` isn't available/on cooldown), then wait for
`DailyReward` to become available again — it should force-close `Offer` and take over.
