# Testing in Unity: the mechanics

What each suite covers and how to extend it is [`../feature-maps/tests.md`](../feature-maps/tests.md).
This file is the harness knowledge — the framework behaviours and traps that decide *how* a test has
to be written here.

## Test bodies are plain `async Task`

The Unity Test Framework has driven those natively since 1.4 (this project is on 1.6), so the older
`[UnityTest] IEnumerator X() => XAsync().ToCoroutine()` pair — two methods per test, purely to bridge
into a coroutine — is gone.

The property that made the bridge necessary still holds and is still relied on: **awaits run under
the editor player loop rather than blocking the test thread.** That matters because
`WindowQueueRunner` suspends on its own wake signal internally, and blocking would deadlock it.

Assertions use NUnit's constraint model (`Assert.That(x, Is.EqualTo(y))`); the classic
`Assert.AreEqual` is gone.

## Never assert an async delegate inside a constraint

```csharp
// Deadlocks the editor rather than failing:
Assert.That(async () => await SomethingAsync(), Throws.InstanceOf<OperationCanceledException>());
```

NUnit resolves an async delegate inside a constraint by **blocking the calling thread** until it
completes — and everything here is a UniTask driven by the player loop, which runs on that same
thread. Await it in the test body and assert on the outcome instead.

It happens to be safe when the call throws before its first yield (an already-cancelled token, say),
but the form is a deadlock waiting for the day someone adds a real await to it. Both suites do this
deliberately and say so.

## Synchronous continuation vs. a bounded wait

Awaiting an **already-completed** UniTask continues synchronously (see
[`unitask-and-cancellation.md`](unitask-and-cancellation.md)). `FakeWindowsManager.OpenAsync` returns
one, so immediately after *calling* — not awaiting — `runner.ShowAvailableWindowsAsync()`, every
`OpenAsync` the runner makes before its first real suspension has already happened. The happy-path
tests assert on `FakeWindowsManager.OpenCalls` with no polling at all.

Anything the runner does *after* suspending needs the bounded wait helper: setting
`FakeWindowQueueAggregator.Available` raises `AvailabilityChanged` and wakes the runner immediately,
but its continuation still runs on a later frame of the editor player loop — not synchronously inside
the setter.

Those helpers are measured with a `Stopwatch`, **not** with the injected clock. This is the harness
giving the runner real frames and real time to act; it is not game logic reading a game clock. Mixing
the two makes a test that either hangs or passes for the wrong reason.

## Negative assertions need a window

"This must NOT happen" has no condition to wait for, so it needs a bounded window in which the wrong
thing could have happened. The trick is to make that window meaningful: set up the state that would
trigger the wrong behaviour, confirm the runner was actually woken by it, and then give it a couple
of hundred milliseconds — many frames of the editor player loop. Since the runner became
event-driven, a wake is immediate, so the window no longer has to cover a poll interval.

## Fakes can encode production behaviour — and then drift

`FakeWindowsManager` mirrors a real ordering constraint of `WindowsManager`: it flips `IsQueueIdle`
before calling `Handle.MarkClosed()`, because `MarkClosed` completes the source
`WindowQueueRunner` is awaiting and that resumption can run synchronously, re-entering the fake and
reading `IsQueueIdle`. Flipping it afterwards would leave the runner reading a stale "not idle" and
silently dropping the item it was about to reconsider.

A fake that encodes production behaviour can drift from production silently. **The PlayMode suite is
the guard against exactly that** — nothing there is faked, so nothing there can drift. A mocking
framework would not have helped with this particular problem, and none is installed.

Two more fake-design rules worth keeping:

- **Leave what you do not model as `null`.** `FakeClockRpcManager` implements only the module
  `ServerSyncedTimeProvider` uses and leaves the other three null on purpose: a test that starts
  reaching for inventory or remote config through it should fail loudly at the null, not quietly
  succeed on invented data nobody chose.
- **Model the shapes the production code treats differently.** `FakeWindowQueueAggregator` can be
  *event-shaped* (something happens, the aggregator says so) or *schedule-shaped* (a timestamp passes
  and nothing is raised at all). While it hardcoded `NextAvailabilityChangeUtc` to null, the runner's
  "sleep exactly until the next scheduled change" branch was executed by **no test at all** — every
  scheduled wake collapsed to the 60-second fallback heartbeat and nothing noticed. A regression
  there would have looked like "the popup shows up about a minute late, sometimes", with nothing red.
- **A gate beats a delay.** The PlayMode remote-config fake is opened by hand rather than answering
  after a timer, so "the window is up while the fetch is still running" is a fact about the test
  rather than a race against a clock. Its teardown *cancels* anything still parked on it, rather than
  completing it, so no controller writes into a view the fixture is about to destroy.
- **Fixed, arbitrary start times.** `FakeTimeProvider` starts at a fixed instant rather than "now",
  so a failure reproduces identically tomorrow. `ServerSyncedTimeProviderTests` puts the fake server
  clock in 2099 deliberately: a device clock near the fake's anchor would let a broken implementation
  pass, and telling the two clocks apart is the whole point.

## Why some things can only be PlayMode

- **A view is only a valid object when it comes from its own prefab.** The views hold their UI parts
  in private `[SerializeField]`s with no null guards — `DailyRewardWindowView.Awake` dereferences
  `_claimButton`, `SetContent` dereferences `_titleText` — so a view built in code throws before a
  single assertion runs. That means the real `IUiPrefabProvider`, which means PlayMode. Treating it as
  a fixture problem rather than a design one is deliberate: adding test-only setters to production
  views to make them constructible is a worse trade than a slower fixture.
- **`UnityWebRequest` needs the player loop to progress** — hence the `RemoteImageLoader` fixture,
  which also exists so the download genuinely happens rather than a fake saying it did.
- **Transitions take real frames.** `OpenAsync` only returns once the open transition has finished,
  so a window still half faded in at that point means the manager reported `Active` too early.

## The PlayMode UI hierarchy is built, not loaded

`TestUiHierarchy` builds a code-only copy of `MainScene`'s `UIRoot` — a screen-space overlay canvas
with the four layers, each with its own `Canvas` + `GraphicRaycaster` and the same sorting bands.

- **Built rather than loaded** so the fixtures own their world completely. Loading `MainScene` would
  also start `AppEntryPoint`, which connects to the fake backend and starts the window queue —
  windows would appear on their own, mid-test. That is worth its own test one day; it is not what
  these fixtures measure.
- **No `EventSystem` or input module**, because `StandaloneInputModule` reads the legacy
  `UnityEngine.Input` and throws every frame under the Input System package (it failed every test in
  the fixture before it was removed). Nothing here simulates input; the input-ordering side of the
  canvas split is asserted through `sortingOrder` instead.
- It implements `IUILayerProvider`, which is the whole reason that port exists: the engine takes the
  port, so a test can supply four plain `Transform`s without reflecting into `UIRoot`'s serialized
  fields or adding a test-only setter to a production type.
- Layer bands are exposed as constants so tests assert against them rather than against literals.
- The fixture reproduces the app's **preload precondition** by loading the backdrop address in
  `SetUp` — `ModalBackdropPresenter` cannot await, and in the app `AppEntryPoint` preloads it. Under
  the editor's asset-database Addressables mode that returns immediately.
- Teardown steps are idempotent, so a test that already drove shutdown itself is fine and one that
  did not still leaves nothing behind.
- `WindowsManager` is typed as the **concrete class** in the fixtures, because two tests drive
  shutdown and `IWindowsManager` deliberately does not expose `Dispose` — tearing the engine down is
  the composition root's business, not something a caller holding the port should be able to do.

## Observing a view from outside

The views expose setters and no getters at all, so the only way to observe one is to read the UGUI
components out of its hierarchy. The PlayMode helpers find buttons **by the label their own
controller wrote**, rather than by name or sibling index, so they survive the prefab being
rearranged.

## The editor-only assembly boundary

`PopupSystem.Tests.EditMode` is Editor-only, and an assembly with no platform restrictions cannot
reference one that has them — so the PlayMode suite cannot reuse the EditMode fakes. See
[`zenject-composition.md`](zenject-composition.md).

## Reading a Play Mode run from outside the editor

`Assets/Editor/PlayModeResultProbe.cs` is a temporary verification helper (delete it once its result
has been read). It lives in a real editor assembly rather than a dynamically compiled command script
because entering Play Mode reloads the domain, which destroys any `TestRunnerApi` callback registered
from a throwaway assembly before `RunFinished` ever fires. `[InitializeOnLoad]` re-registers on every
load, so the callback is there when the run ends.
