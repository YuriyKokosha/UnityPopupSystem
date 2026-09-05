# Resilience: what degrades, what is contained, what is hard

`CLAUDE.md` §8 states the rule — *errors degrade, they don't crash the loop*. This is the
site-by-site reasoning behind it. The shape is always the same question: **if this fails, what stops
working, and for how long?**

## Failures that must never end a loop

A loop that dies takes every *future* occurrence with it, and the symptom is silence rather than an
error. Three loops are guarded from the inside:

- **`WindowQueueRunner.MonitorIdleAsync`** — the `try/catch` lives *inside* the `while`, not around
  it. An exception escaping a single pass must not end idle monitoring for the rest of the session;
  that would silently stop every future popup (daily reward, offer, …) from ever appearing, with no
  symptom beyond a console log.
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

`WindowQueueRunner` keeps two sets, and the difference between them is the whole policy:

- **`_failedThisBurst`** — per burst. A window whose turn threw (bad payload, a bug in its own
  `Init`) is suppressed for the remainder of *this* burst so it cannot retry-loop, and gets another
  chance on the next scan in case the cause was momentary. A single misbehaving window must not take
  the rest of the queue down with it: the burst falls through to the next-highest item.
- **`_presentedSinceAvailable`** — outlives a burst deliberately. See
  [`../feature-maps/window-queue.md`](../feature-maps/window-queue.md) for the starvation and
  reopen-loop problems it solves.

A window that failed to open gets **neither** `MarkShown` **nor** a `_presentedSinceAvailable`
entry. Neither cooldown timing nor "it had its turn" should advance for a window that never
appeared — otherwise one failed open sends it quiet for a cooldown it never earned.

Similarly, a *misbehaving close transition* is logged and the instance still proceeds to disposal.
It must not prevent disposal, and it must not vanish silently either, or a broken close animation
leaves no trace anywhere.

## Failures that degrade the content, not the feature

- **Remote config unreachable** → `OfferManager` serves `FallbackContent`: generic-but-correct copy
  instead of a broken or empty popup. Crucially the offer stays *usable* — the terms are local, only
  the copy was remote.
- **Banner missing or unreachable** → `OfferWindowController` falls back to the no-banner layout.
  The copy and the buy button are still fully usable, just without the promo image. `OfferData`
  documents that `BannerUrl` may be null/empty; callers must handle that rather than assume a URL.
- **A request that succeeded but returned a non-image body** (wrong content-type, corrupted file) is
  treated as a load failure, rather than silently handing the caller a null texture it was not
  expecting.
- **A missing banner must surface as a failure the caller can degrade on**, not as a null texture
  handed back as though it had worked. That is the difference between a fallback layout and a blank
  rectangle.
- **`PreloaderOverlayView`** guards every serialized reference. A prefab missing one degrades the
  way the rest of the view does, instead of throwing during boot from inside the container.

## Failures that are deliberately hard

Not everything should degrade. Two things fail loudly and immediately:

- **An unresolvable prefab address.** There is no code-built fallback window; see
  [`addressables-and-content.md`](addressables-and-content.md). A caller handed `null` fails later
  and further from the cause.
- **A view whose type does not match its `WindowDefinition.ViewType`.** Checked in
  `WindowFactory` before anything reaches the screen, so the error can name the window type, the
  expected type and the address that produced the wrong prefab.

And one thing that must not be abandoned at all: a claim or a purchase already in flight. See
[`unitask-and-cancellation.md`](unitask-and-cancellation.md) — a claim is all-or-nothing, and
"cooldown started, nothing granted" is the shape of bug report nobody can reproduce.

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
