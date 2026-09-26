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
but the form is a deadlock waiting for the day someone adds a real await to it — even where the
fakes happen to complete synchronously, await in the body and assert on the captured exception.

## Expected exceptions carry an `[Expected]` label

Many tests inject a failure on purpose — a throwing observer, a failing load, a full fake disk — and
assert that production *logs* it rather than swallowing or propagating it. Those logs are red in the
console, so every exception a test or fake throws on purpose starts with `[Expected] `, and its
`LogAssert.Expect` matches the label too (`new Regex(@"\[Expected\] slot view failure")`). A red entry
without the label after a run is worth reading; one with it is part of a test.

The label marks injected failures only. When a scenario makes production log its own exception (the
offer flow's "Not enough gold"), the text stays production's and the `Expect` carries a comment saying
so. Guard-rail exceptions in fakes ("These flows never call Core.") are *not* labelled: if one ever
shows up, it is a bug in the test. Whether labelled or not, a log no `Expect` declared fails the test.

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
of hundred milliseconds — many frames of the editor player loop. The runner is event-driven, so a
wake is immediate and the window does not have to cover a poll interval.

## Fakes can encode production behaviour — and then drift

`FakeWindowsManager` mirrors a real ordering constraint of `WindowsManager`: it flips `IsQueueIdle`
before calling `Handle.MarkClosed()`, because `MarkClosed` completes the source
`WindowQueueRunner` is awaiting and that resumption can run synchronously, re-entering the fake and
reading `IsQueueIdle`. Flipping it afterwards would leave the runner reading a stale "not idle" and
silently dropping the item it was about to reconsider.

A fake that encodes production behaviour can drift from production silently. **The PlayMode suite is
the guard against exactly that** — nothing in the window engine is faked there, so nothing there
can drift. A mocking
framework would not have helped with this particular problem, and none is installed.

Drift runs both ways: a fake *stricter* than production keeps every EditMode test green while
production is wrong. So the rule is: **when a fake hard-codes an ordering, a PlayMode test pins the
same ordering on the real `WindowsManager`, in the same change.** Example: `FakeWindowsManager.OpenAsync`
reports busy synchronously, and
`WindowsManagerPlayModeTests.OpenAsync_CountsTheWindowAsBusy_WhileItsPrefabIsStillLoading` (through
`ScriptedPrefabProvider`, which holds a real load for one frame) holds production to the same promise
(`_pendingOpens`); without it the queue could open its own window on top of one whose prefab is still
loading. When you change either side, read the fake and the production code side by side.

Two more fake-design rules worth keeping:

- **Leave what you do not model as `null`.** `FakeClockRpcManager` implements only the module
  `ServerSyncedTimeProvider` uses and leaves the other four null on purpose: a test that starts
  reaching for the wallet, inventory, the queue or remote config through it should fail loudly at
  the null, not quietly succeed on invented data nobody chose.
- **Model the shapes the production code treats differently.** `FakeWindowQueueAggregator` can be
  *event-shaped* (something happens, the aggregator says so) or *schedule-shaped* (a timestamp passes
  and nothing is raised at all). A fake that hardcodes `NextAvailabilityChangeUtc` to null leaves the
  runner's "sleep exactly until the next scheduled change" branch executed by **no test at all** —
  every scheduled wake collapses to the 60-second fallback heartbeat and nothing notices. A regression
  there looks like "the popup shows up about a minute late, sometimes", with nothing red.
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
  windows would appear on their own, mid-test. That is not what these fixtures measure.

The scene itself has its own fixture, `MainSceneBootPlayModeTests`: it loads `MainScene` additively,
polls (with a `Stopwatch`-bounded wait) until the main screen is up, checks the preloader is hidden and
the scene's `EventSystem` carries an input module, waits for the startup queue burst to settle, and
unloads the scene in a `finally`. Any error or exception logged during boot fails it, which is what
makes it a check on `SceneContext`, `AppInstaller` and the serialized references. It asserts no
input — nothing in either suite simulates clicks. It must not touch a developer's save, so before
the load it sets Zenject's `SceneContext.AfterInstallHooks` (run once, after `AppInstaller`, then reset
by Zenject) to re-bind `IInventoryStorage` to the production `FileInventoryStorage` on a scratch
directory; it asserts that the save landed there and that `persistentDataPath` is unchanged. Prefer
this one-binding override to editing `AppInstaller` for tests.
- **No `EventSystem` or input module**, because `StandaloneInputModule` reads the legacy
  `UnityEngine.Input` and throws every frame under the Input System package, failing every test in
  the fixture. Nothing here simulates input; the input-ordering side of the
  canvas split is asserted through `sortingOrder` instead.
- It implements `IUILayerProvider`, which is the whole reason that port exists: the engine takes the
  port, so a test can supply four plain `Transform`s without reflecting into `UIRoot`'s serialized
  fields or adding a test-only setter to a production type.
- Layer bands are read back through `GetLayerSortingOrder(layer)` so tests assert against the
  hierarchy rather than against literals.
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

## Reading a test run from outside the editor

`Tools/Tests/Run EditMode|PlayMode tests (write results)` (`Assets/Editor/TestRunMenu.cs`) starts a
suite through `TestRunnerApi`, and `Assets/Editor/PlayModeResultProbe.cs` (both in the
`PopupSystem.EditorTools.TestRunner` assembly, so nothing of ours lives in `Assembly-CSharp-Editor`)
writes the outcome to
`Claude outputs/TestResults/<mode>.txt` (summary line, then one line per non-passing test) and logs it
to the console as `[PlayModeResultProbe] <mode>: passed=… failed=…`. The two are split on purpose: a
PlayMode run reloads the domain, which destroys any callback registered by the menu item before
`RunFinished` fires, so the reporter lives in an `[InitializeOnLoad]` type that re-registers on every
load. The file starts as `running` and is overwritten when the run ends — a file that stays at
`running` means the run never finished.

## When the editor stops responding during a PlayMode run

It is almost certainly a **deadlock on the main thread**, not a slow test, and the usual culprit is the
async-delegate-inside-a-constraint form described above — most dangerously around code that hops to
the thread pool (`UniTask.RunOnThreadPool`, as `FileInventoryStorage.LoadAsync` does), because it
has to come *back* to the main thread NUnit is blocking. Symptoms: the run never finishes, the result
file stays at `running`, and the MCP bridge stops answering too; only a force-quit recovers the editor.
Look for a `Throws` constraint around an async call; the fix is always the same: await in the body,
capture the exception, assert on the captured value.
