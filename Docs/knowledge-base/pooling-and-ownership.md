# Pooling, ownership and close ordering

Three families of trap that share a cause: something outlives the thing that created it.

## View pooling

`WindowFactory` pools closed `WindowView`s per `WindowType` instead of destroying them. Windows like
`Settings`/`DailyReward`/`Offer`/`RewardPopup` are opened and closed many times over a session, so
recycling the view is a real recurring saving. Only the `WindowController` is ever fresh per open —
it holds the per-open state and is a cheap plain object.

The pool is capped (`MaxPooledPerType`). It used to be unbounded and never emptied, which is a leak
wearing a pool's clothes: nothing in this system shows two windows of the same type at once, so one
spare covers every reopen and the second is slack for the moment an outgoing view is still animating
out as the next is acquired. Past that, holding a view is strictly worse than instantiating one.
`WindowFactory.Dispose` destroys whatever the pool still holds.

### What a pooled view brings back with it

**A pooled view keeps its previous sibling index.** A freshly instantiated view always lands as the
last sibling, which is what makes "most recently opened" render on top and what the backdrop and
stacking logic rely on (they read sibling index to find the topmost active instance per layer).
Without an explicit `SetAsLastSibling`, a pooled view could reappear underneath windows opened after
it. Re-parenting is done for the same reason: layer assignment is static per `WindowDefinition`
today, so it is a no-op — but it costs nothing and removes a subtle bug if that ever changes.

**A pooled view was last seen mid-close.** `FadeScaleWindowTransition.PlayCloseAsync` turned
`CanvasGroup.blocksRaycasts` and `interactable` off; `PlayOpenAsync` turning them back on is
therefore load-bearing, not defensive tidiness. Without it a reopened window renders and ignores
every click. The open transition also fades from wherever alpha currently is rather than a hardcoded
1, because a window interrupted mid-open may still be mid-fade-in.

**A pooled view keeps whatever its controller left behind.** `WindowView.ResetForPool()` is called
by `WindowFactory` right before a view is stashed — never on an ordinary `Hide()`/`PlayCloseAsync`.
The base implementation does nothing, because most views hold no per-open state beyond what their
next controller overwrites on `Init`. Override it when a view holds something a fresh controller
will *not* reset: `OfferWindowView` clears its banner reference, or the pooled instance would flash
the *previous* offer's banner underneath the "Loading banner…" indicator on the next open.

**Controller-level state disabled "for the rest of this window's life" comes back disabled.**
`DailyRewardWindowController` disables its claim button after a claim and never re-enables it,
because the window closes right after. So its `OnInitializeAsync` restores a clean baseline on every
init rather than assuming a fresh view. A regression here is invisible on the first open and bricks
every one after it.

Pooling a view at shutdown is pointless — `WindowFactory.Release` skips it while
`WindowsManager.Dispose` is tearing the last windows down, since a reopen will never come and it
would only keep a GameObject alive.

## Who owns a downloaded texture

`RemoteImageLoader` caches textures by resolved URL, keeps them for the life of the session, and
destroys them together on `Dispose`. The same shape as the prefab provider, and for the same
reason: the offer popup is opened, closed and opened again, and re-downloading its banner every
time is what a cache exists to avoid.

That is a **change of ownership**, and it is the thing to know when touching either consumer. Each
download used to belong to whoever asked for it, so `OfferWindowView` destroyed the texture on pool
reset and `OfferWindowController` destroyed it on the cancellation path. Both would now leave a
destroyed entry in the cache, to be handed to the next window asking for the same URL.

**Consumers display; the loader owns.** Corollaries:

- `OfferWindowView` detaches the reference and does not destroy.
- The controller's "download finished in the same frame the window was dismissed" branch has
  nothing to clean up.
- If two loads of the same URL are in flight (two windows asking at once, or a reopen during a slow
  download), exactly one texture is published per URL and the loser is destroyed. Two would leave
  whichever the views drop alive for the rest of the session — the leak the cache is meant to
  remove.
- A `Texture2D` can be destroyed out from under its dictionary entry, so a cache hit is null-checked
  before being handed out.
- Textures are created `nonReadable`. The CPU-side copy is only needed by code that reads pixels
  back, and nothing here does — the texture goes straight onto a `RawImage`. Leaving it readable
  costs a second copy of every banner in system memory on top of video memory.
- A request needs a timeout. Without one, a request that never answers — captive-portal Wi-Fi, a CDN
  black-holing the connection — hangs until the window that asked closes, so the offer sits on its
  loading spinner until the player gives up instead of falling back to its no-banner layout.

## Close ordering: two constraints that look removable

Both live in `WindowsManager.CloseInstanceAsync` and both are mirrored by `FakeWindowsManager` in
the EditMode suite.

**1. Stop counting the window as busy *before* completing the handle.** `MarkClosed()` resumes
everyone awaiting `WaitForCloseAsync()`, and that resumption can run **synchronously**. If it read
`IsQueueIdle` while the handle were still in `_closingHandles`, it would see a stale "not idle" and
silently drop the item it was about to reconsider. The stacks already have the same ordering
constraint.

**2. Raise `QueueBecameIdle` *after* `MarkClosed()`.** By then anyone resuming from
`WaitForCloseAsync` has already run, and any window they opened in response is accounted for —
otherwise the event announces an idle screen that is about to stop being idle.

Related: a window still playing its close transition is off the stacks but visibly present, so
`_closingHandles` keeps it counted as busy. Treating it as idle lets the next window open on top of
one that is still animating out.

## The double-close race

Two closers can race — the queue interrupting a window at the same moment the player taps its close
button. The second `CloseAsync` must **not** return `UniTask.CompletedTask`: that tells the caller
the window is gone while it is still on screen mid-transition, and the queue runner would then open
the next window straight over it. It hands back a wait for the close that is genuinely in flight
instead.

Re-entrancy is guarded the instant closing starts, before anything yields, or a second close request
arriving mid-close runs the whole teardown twice.

## Aborting an open

Cancellation or failure while opening happens for real now that transitions take frames. The
instance was already pushed onto its stack (or set as `_baseWindow`) *before* `OpenInstanceAsync`
started, so — unlike an explicit close — nobody has removed it from tracking yet.
`AbortOpeningInstanceAsync` does that first: without it, a closed-but-still-in-the-stack instance
wedges `IsQueueIdle` permanently and leaves the backdrop computation wrong.

A window whose init or open transition is cancelled or throws therefore goes straight from
`Initializing`/`Opening` to `Closing` and then `Disposed`, never reaching `Active`. Test for the
lifecycle stage you care about; never infer that its predecessor ran.

## Reused scratch containers

`WindowsManager` and `ModalBackdropPresenter` keep a few reusable lists/stacks as fields rather than
allocating per call. Opening and closing a window are the two operations that already do the most
work per frame, and allocating a container to hold a handful of references — then throwing it away
in the same method — is garbage generated by the act of closing a window.

It is safe **only** because everything those containers hold lives between two awaits and never
across one. `CollectActiveInstances` exists for the same reason: two consumers follow it, and a
`yield return` enumerator allocated one iterator per consumer on every open and close.
