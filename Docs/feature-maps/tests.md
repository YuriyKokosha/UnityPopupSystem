# Feature map — Tests

**Folders:** `Assets/Tests/EditMode/**` (assembly `PopupSystem.Tests.EditMode`),
`Assets/Tests/PlayMode/**` (assembly `PopupSystem.Tests.PlayMode`)
**Covers:** `Game.Services.WindowQueue.WindowQueueManager`,
`Game.Services.WindowQueue.WindowQueueRunner`, `Game.Services.DailyReward.DailyRewardManager`,
`Game.Services.Time.ServerSyncedTimeProvider`, `UI.Runtime.Manager.WindowsManager`,
`UI.Runtime.Factory.WindowFactory`, `UI.Services.RemoteImageLoader`, and the concrete window
controllers' own flows
**CI:** `Docs/ci/` — GameCI workflow running both suites; see that folder's README (it still has to
be moved into `.github/workflows/`).
**Related knowledge base:** [`testing-in-unity.md`](../knowledge-base/testing-in-unity.md) — the
harness mechanics: async test bodies, the NUnit constraint deadlock, synchronous continuation vs a
bounded wait, negative assertions, fake-design rules, why some things can only be PlayMode. Read it
before writing a test here.

## The split: what belongs where

| | EditMode | PlayMode |
|---|---|---|
| Assembly | `PopupSystem.Tests.EditMode` | `PopupSystem.Tests.PlayMode` |
| References (ours) | `Contracts`, `Game` | `Contracts`, `Core`, `Game`, `UI` |
| Subject | the queue, and the `Game` services it reads | the window engine, and the concrete window flows |
| Collaborators | fakes | only the backend and the clock are faked |
| Needs | nothing — no scene, no container, no prefabs | a canvas, a DI container, real Addressable prefabs, real frames |

The dividing line is not "fast tests here, slow tests there". It is what the subject actually
needs. The queue is plain C# that talks to an interface, so faking that interface costs nothing and
buys millisecond tests. The window engine instantiates prefabs, parents them under a canvas and
animates them over real frames — faking any of that would mean testing the fake. And a *view* is
only a valid object when it comes from its own prefab, which is what pushes the controller-flow
fixture into PlayMode too.

Note what the EditMode assembly does **not** reference: `PopupSystem.UI`. The queue is testable
without the window engine at all, which is the layering claim in `CLAUDE.md` §3 stated as a build
fact rather than a convention. Note also what follows from it: `PopupSystem.Tests.EditMode` is an
Editor-only assembly, so the PlayMode suite cannot reuse its fakes.

## EditMode

Four fixtures, all plain `async Task` or synchronous `[Test]`, NUnit constraint model throughout.

| Fixture | Subject |
|---|---|
| `WindowQueueManagerTests` | Priority ordering, cooldown gating and expiry, the `SetItems`/`MarkShown` bookkeeping that drives them, and `TimeUntilCooldownReady` (a different question from `IsCooldownReady`, and the one the runner's pacing depends on). |
| `WindowQueueRunnerTests` | The runner's behaviour against fakes: selection, idle-gating, interrupts, suppression, re-arming, re-entrancy, config refresh, scheduled wakes. |
| `DailyRewardManagerTests` | The availability contract `DailyRewardWindowAggregator` reads, plus the all-or-nothing property of a cancelled claim. Worth its own fixture because the aggregator is a two-line adapter: every decision the queue makes about the daily reward is really made here. |
| `ServerSyncedTimeProviderTests` | The production `ITimeProvider`. Every cooldown, the daily reward and the offer's active window read this one class, so "does it actually stop reading the device clock" is the single assertion the rest of the queue's correctness rests on. |

`WindowQueueRunnerTests` has a `[TearDown]` that disposes the runner. That is not tidiness: the
runner subscribes to every aggregator and to `IWindowsManager.QueueBecameIdle` in its constructor,
so a leaked one would sit there reacting to the *next* test's fakes.

### Fakes (`Tests/EditMode/Fakes/`)

| Fake | Stands in for | Notes |
|---|---|---|
| `FakeTimeProvider` | `ITimeProvider` | A clock the test moves by hand, starting at a fixed arbitrary instant so a failure reproduces identically tomorrow. This is what makes cooldown *expiry* testable at all. |
| `FakeClockRpcManager` | `IRpcManager` | The smallest manager that satisfies `ServerSyncedTimeProvider`, which only calls `Core.GetServerTimeUtcAsync`. The other three modules are left **null on purpose**: a test that starts reaching through this fake should fail loudly, not quietly succeed on invented data. |
| `FakeWindowQueueAggregator` | Any real `IWindowQueueAggregator` | Can take **either of the two shapes** the real ones have, because the runner treats them completely differently. *Event-shaped* (like `DailyRewardWindowAggregator`): set `Available`, and the setter raises `AvailabilityChanged`. *Schedule-shaped* (like `OfferWindowAggregator`): set `AvailableFromUtc`, and availability flips because the injected clock passed a timestamp with **nothing raised at all** — `NextAvailabilityChangeUtc` is then the only thing that can wake the runner. Setting both would model neither, so `AvailableFromUtc` wins. |
| `FakeWindowsManager` | `IWindowsManager` | Records every `OpenAsync` (`OpenCalls`) and hands back a **real** `WindowHandle` wired to a close callback, so a test drives "the player closed the window" — or the runner force-closing it — with `handle.CloseAsync()`. `OpenExceptionFor` makes one window type fail to open, which is the only way into the runner's per-burst failure suppression. `CloseCurrentWindowAsync`/`CloseTopPopupAsync` are no-ops: the runner always closes through the handle. |

Two things about that last row are load-bearing and easy to break. The close callback flips
`IsQueueIdle` **before** calling `MarkClosed()`, mirroring production; and `OpenExceptionFor`
*throws* rather than returning a faulted task, because a real window blowing up in its own
construction does so synchronously too, before `OpenAsync` ever yields. Both, and the
fake-can-drift-from-production problem in general, are in
[`testing-in-unity.md`](../knowledge-base/testing-in-unity.md).

`FakeWindowQueueAggregator`'s schedule-shaped mode is the reason the fake needed a clock at all.
While `NextAvailabilityChangeUtc` was hardcoded to null, `WindowQueueRunner.NextScheduledWakeMs`
collapsed to the 60-second fallback heartbeat in **every single test** — so the half of the runner
that replaced polling was executed by nothing.

Access to `WindowHandle`'s `internal` members is granted by
`[assembly: InternalsVisibleTo("PopupSystem.Tests.EditMode")]` in
`Assets/Scripts/Contracts/AssemblyInfo.cs`, which names this assembly and `PopupSystem.UI`
explicitly rather than opening every internal to the whole editor-default assembly.

## PlayMode

Three fixtures. Everything above the `Game` services is real: real `WindowsManager`, real
`WindowFactory`, real prefabs by Addressables address, real transitions over real frames. Only the
backend and the clock are faked, because those are the seams whose behaviour a test needs to
*choose* — a failing endpoint, a banner that never arrives, an offer window that has closed.

| Fixture | Subject |
|---|---|
| `WindowsManagerPlayModeTests` | The engine itself. Because the provider is the real one, this is also the only automated check that the addresses in the `WindowDefinition`s actually resolve — and the honest answer to the fake-drift criticism of the EditMode suite: nothing here is faked, so nothing here can drift. |
| `WindowFlowPlayModeTests` | The concrete window controllers' flows: claim, purchase, remote content, and what each does when the thing it is waiting for fails or arrives too late. These flows are exactly where the runtime defects in `Docs/review-response.md` §4 lived. |
| `RemoteImageLoaderPlayModeTests` | The real `RemoteImageLoader` against the real StreamingAssets file the demo serves its offer banner from — the same "remote" endpoint `AppInstaller` wires up. PlayMode because `UnityWebRequest` needs the player loop, and because the point is that the download actually happens. |

`TestUiHierarchy` is the shared world: a code-built copy of `MainScene`'s `UIRoot`, implementing
`IUILayerProvider`. Why it is built rather than loaded, why it carries no `EventSystem`, and the
rest of the fixture setup rules (the preload precondition, idempotent teardown, typing
`WindowsManager` as the concrete class) are in
[`testing-in-unity.md`](../knowledge-base/testing-in-unity.md).

`WindowsManagerPlayModeTests` keeps its container small on purpose — `Settings` (a plain `Window`,
non-modal, `WindowsLayer`, with a transition), `RewardPopup` (a `Popup`, modal, close-on-backdrop,
`PopupsLayer`) and `MainGame` (the base screen) cover every interesting shape.

## Coverage map

**`WindowQueueManagerTests`** — priority-descending / `WindowType`-ascending tiebreak ordering;
`SetItems(null)` clears; `GetItemsSortedByPriority` returns the *same instance* until the item set
changes, and excludes types the latest call dropped; cooldown gating (never shown → ready; just
shown with a positive cooldown → not ready; just shown with a zero cooldown → ready; per-type);
cooldown expiry — still gated one second short, ready exactly at the boundary, restarted by every
`MarkShown` rather than measured from the first; `SetItems` **keeps** cooldown state for types still
in the config and **drops** it for types that left; `SetItems` raises `ItemsChanged`;
`TimeUntilCooldownReady` null when never shown, null for a zero cooldown even after `MarkShown`,
the remainder while running, null once elapsed. Every expiry case runs on `FakeTimeProvider` and
takes no wall-clock time.

**`WindowQueueRunnerTests`** — opens the highest-priority *available* window (not the
highest-priority *configured* one) and completes once it closes; does nothing while the queue is not
idle; skips an unavailable aggregator, and an item with no aggregator at all, rather than throwing
or hanging; a concurrent second call during a burst is a same-frame no-op; a non-interruptible window
is never force-closed; an interruptible one *is* force-closed and then reconsidered as a brand-new
handle once the interrupting window closes, with its cooldown bookkeeping untouched; a window with a
popup of its own open is never force-closed; a dismissed window does not reopen while its
availability has not changed, but does once availability drops and returns, or once a positive
cooldown elapses; the runner wakes at the *scheduled* moment when availability changes with nothing
to raise an event; it still wakes promptly after an interrupt cycle has overlapped two waiters
(a 5-second bound on a path whose broken worst case is 60 seconds); a window that throws while
opening is skipped for the burst **without** advancing its cooldown or its "had its turn" state, and
the burst falls through to the next item; a window dropped from the config and brought back loses
its already-shown state, while a refresh that still contains a window keeps suppressing it; and a
config arriving while the runner is parked on its longest sleep wakes it.

**`DailyRewardManagerTests`** — available from the very first frame (`NextAvailableAtUtc` starts at
`MinValue`, not "now", because the constructor runs before the clock is synced); a claim makes it
unavailable and raises `AvailabilityChanged`; it comes back once the interval elapses, with nothing
raised at that moment — which is exactly why the aggregator reports `NextAvailableAtUtc` instead; a
cancelled claim leaves availability untouched (all-or-nothing).

**`ServerSyncedTimeProviderTests`** — falls back to the device clock before the first sync (the
deliberate degradation: time frozen at `MinValue` would report every cooldown as expired); reports
server time after sync, decades away from the device clock; *advances* from the anchor rather than
being frozen at it (a provider returning the anchor verbatim would make every cooldown eternal and
would pass the previous test); a second sync **replaces** the anchor rather than adding to it.

**`WindowsManagerPlayModeTests`** — `OpenAsync` instantiates the real prefab into its own layer and
reaches `Active`; the real transition has finished by the time it returns (a manager reporting
`Active` early would show here); `CloseAsync` runs through to `Disposed`, leaves the queue idle and
hides rather than destroys the view; reopening the same type reuses the pooled `GameObject`; a popup
opens on its own layer sorted above the window beneath it; a modal popup gets a backdrop sorted
*between* it and the rest of its layer, a non-modal window gets none, and closing the popup takes
its backdrop with it; `CloseTopPopupAsync` closes only the popup; closing the same handle twice
waits for the close already in flight rather than reporting "already gone" mid-animation;
`StateChanged` announces `Closing` before `Disposed` without skipping either (the lifecycle was a
claim nothing checked — `WindowHandle.StateChanged` had no subscriber anywhere in the project);
the base screen hands back the same handle when opened again while up and never counts as queue-busy,
even after being closed and reopened; `Dispose` tears down everything still on screen, releasing
every controller and completing every handle anything was parked on; and disposing the factory
destroys what the pool was holding.

**`WindowFlowPlayModeTests`** — the claim opens the reward popup *while the claim is still in
flight* (which is what makes the popup's loading state a real signal); a `DailyReward` window
reopened from the pool has its claim button enabled again (invisible on the first open, bricks every
one after); the offer opens with a placeholder and then fills in the remote copy; it falls back to
safe copy when the remote-config endpoint fails, and stays *usable* because the terms are local;
it reports no active offer once its window has closed (a branch that was unreachable until
`OfferManager` stopped deriving both ends of the window from "now"); an offer closed while its
content is still in flight never writes into the torn-down view; and the reward popup shows an error
when the operation it observes *fails* but says nothing when that operation is *cancelled*.

**`RemoteImageLoaderPlayModeTests`** — the same texture comes back for a repeated URL (without the
cache, every reopen was a fresh download and a fresh allocation); `Dispose` destroys the cached
textures, which is now the *only* place they are destroyed (if this regresses to "nobody destroys
them", every banner ever downloaded outlives the session); and a missing address throws, so the
caller can degrade to the no-banner layout rather than being handed a null texture as though it had
worked.

## Extending this

If you touch `WindowQueueManager` or `WindowQueueRunner`, add an EditMode test; if you touch
`WindowsManager`, `WindowFactory` or a window controller's flow, add a PlayMode one. Both fixtures
already make that cheap.

Gaps worth closing next, in rough order of value:

- **A boot fixture** that loads `MainScene` and asserts the app reaches its main screen. That is the
  one thing the PlayMode suite deliberately does not do, because loading the scene would start the
  queue mid-test.
- **Three queue items.** Today's runner tests only ever exercise two, so a tie or a chain of
  interrupts is untested.
- **The fallback heartbeat.** By construction the one path no test exercises, and exactly what would
  mask a future aggregator that forgets to raise `AvailabilityChanged`.

Run them from Unity's **Window → General → Test Runner** (EditMode and PlayMode tabs), or let CI do
it — see `Docs/ci/`.
