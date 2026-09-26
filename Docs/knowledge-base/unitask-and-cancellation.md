# UniTask and cancellation

The project uses UniTask everywhere (no coroutines, no bare `Task`) and every async API takes a
`CancellationToken`. The interesting decisions are not about the library but about *which* token
goes where — and one library limitation that has its own extension method.

## `Share()`, not `Preserve()`, for a task with concurrent consumers

`UniTask.Preserve()` only supports being re-awaited **after** it has already completed; the cached
result or exception is then replayed synchronously to whoever awaits next. While the task is still
pending, `Preserve()`'s `MemoizeSource` forwards each `OnCompleted` straight through to the
single-continuation-slot source underneath, so a second **concurrent** await — a different
in-flight consumer, before the first completes — throws
*"Already continuation registered, can not await twice"*.

`UniTaskShareExtensions.Share()` (in `Assets/Scripts/Extensions/`, assembly `PopupSystem.Core`)
covers the case this project actually has: a popup driving its own loading state from a
claim/purchase while the code that opened the popup is also awaiting the outcome to restore its own
button. It awaits the source exactly once internally and republishes through a
`UniTaskCompletionSource`, whose `OnCompleted` genuinely supports any number of concurrently
registered continuations.

It lives under `Extensions` rather than `UI` because it is a general-purpose UniTask utility with no
UI dependency — any layer may use it.

## Which token a call gets

The rule is not "pass the token you have". It is: *what should stop this operation?*

**A window's lifetime token** is right for anything whose only purpose is to put something on that
window's screen — a remote-config fetch, a banner download, an open transition. The window going
away is a genuine reason to stop.

**`CancellationToken.None` is right for a transaction**, and this is deliberate at two call sites
(`DailyRewardWindowController` calling `DailyRewardManager.ClaimRewardAsync`, `OfferWindowController`
calling `OfferManager.PurchaseOfferAsync`). The
tidy-looking move is to pass the same token every other async call in the method uses. It would be
wrong: cancelling a claim because the player closed the window means a backend that already granted
the reward and a client that never recorded it — or a payment taken for something the player never
receives. A window closing is a reason to stop *looking at* the outcome (which is what the
`IsClosed` checks after the await do); it is not a reason to abandon the operation.

What a real client would pass here is an **app-lifetime token**, so that quitting the app ends the
wait. This build has no such scope, and inventing one for two call sites would be worse than
recording the reasoning.

The managers take the token anyway, because that method is where a real backend call would go and
an HTTP call needs one. Today it wraps only a simulated delay before a local mutation — there is no
purchase/claim RPC yet ([`../feature-maps/game-services.md`](../feature-maps/game-services.md)). If
cancellation does arrive, the claim is all-or-nothing: nothing is applied until the (simulated) call
itself has gone through. The opposite — cooldown started, nothing
granted — is the shape of bug report nobody can reproduce.

**A non-cancellable token is mandatory on the close path.** `WindowsManager` awaits
`View.PlayCloseAsync(CancellationToken.None)` because `Closing` must always reach `Disposed`, even
when the instance's lifetime token was cancelled mid-open. `IWindowTransition.PlayCloseAsync`
therefore never has to handle cancellation, while `PlayOpenAsync` must let it propagate — yielding
with the token is exactly what makes an interrupted open throw `OperationCanceledException` and
gives `WindowsManager` its abort path.

## Cancelled is not failed

Several catch blocks separate `OperationCanceledException` from everything else, and the distinction
is user-visible:

- Telling the player *"couldn't claim your reward, please try again"* for an operation that was
  abandoned rather than rejected is a lie, and one that invites them to retry something that may
  well have gone through. `RewardPopupController` keeps its loading state instead; whoever cancelled
  the operation owns what happens to the window.
- An abandoned claim or purchase has nothing to report and nothing to restore. Rethrowing would
  only log an "unobserved exception" for an ordinary outcome.
- At startup, `OperationCanceledException` means the app went away while it was still booting (the
  container disposed, cancelling the connect state's retry loop). That is a normal shutdown, and
  logging it as an error would put a red line in the console every time someone leaves Play Mode
  during the loading screen.

## `async void`, `.Forget()` and fire-and-forget

Startup must not run as `async void`: that makes a failure anywhere in the boot chain unobservable —
a missing prefab or a broken installer binding throws into nobody, leaving the player on whatever
happens to be on screen with nothing in the log tying it to startup. Booting is exactly where a
failure has to be loud, because everything diagnosed afterwards depends on knowing the app never
finished starting.

`AppEntryPoint.Initialize()` (Zenject's `IInitializable`, synchronous by contract) calls a
`UniTaskVoid` with a real try/catch: cancellation is silent, anything else is logged as an error
naming startup. That is the general pattern — `async void` never appears in this codebase; a
genuine fire-and-forget call site uses `.Forget()` on a `UniTaskVoid` that handles its own
exceptions.

## Two properties this project's tests rely on

- Awaiting an **already-completed** UniTask continues synchronously, the same way any C# async
  method runs synchronously up to its first real suspension point. So immediately after *calling*
  (not awaiting) `runner.ShowAvailableWindowsAsync()`, any `OpenAsync` it makes before hitting a
  real await has already happened.
- Awaits in a Unity test run under the **editor player loop** rather than blocking the test thread.
  Anything the runner does *after* suspending lands on a later frame, not synchronously inside the
  setter that woke it — hence the bounded wait helpers. See
  [`testing-in-unity.md`](testing-in-unity.md) for the NUnit deadlock this same property causes.

## Waiting rather than polling

A wake signal built from a `UniTaskCompletionSource` needs a companion flag. A signal can arrive
while nobody is parked on the source — between two waits — and dropping it means sleeping through a
change that already happened. `WindowQueueRunner` keeps `_wakeRequested` alongside `_wakeSource` for
exactly that, and the next wait consumes the flag and returns immediately. The overlapping-waiter
hazard that comes with sharing those two fields is documented in
[`../feature-maps/window-queue.md`](../feature-maps/window-queue.md).
