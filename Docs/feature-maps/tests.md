# Feature map — Tests

**Folders:** `Assets/Tests/Editor/**`
**Covers:** `Game.Services.WindowQueue.WindowQueueManager`, `Game.Services.WindowQueue.WindowQueueRunner`

## Why only the queue/priority system has automated tests

The assessment's own evaluation criteria calls out one thing by name for verification: "evidence
that the core logic — especially the queue and priority system — is verified and reliable." That
logic is also, not coincidentally, the part of the codebase with the least Unity-object surface
area — `WindowQueueManager` is plain C#, and `WindowQueueRunner` only touches Unity through
`UniTask`/`Debug.LogException` — so it is the part that is cheapest to test thoroughly without a
scene or a DI container. `WindowsManager`, `WindowFactory`, and the concrete window
controllers/views are exercised manually via the demo scene instead; see `CLAUDE.md` §10 for that
as a named prototype gap, not an oversight.

## How the async tests work

Every `WindowQueueRunner` test method is an `async UniTask`, bridged into Unity's `[UnityTest]`
`IEnumerator` contract via `UniTask.ToCoroutine()` (from the UniTask package) — this drives the
UniTask-based production code frame-by-frame under the real (Edit Mode) player loop, instead of
blocking the test thread on the task (which would deadlock, since `WindowQueueRunner` itself
awaits real-time things like `UniTask.Delay`). `ToCoroutine()` with no `exceptionHandler`
re-throws any exception — including a failed `Assert` — out of `MoveNext()`, so assertion
failures correctly fail the test.

A property the happy-path tests lean on: **awaiting an already-completed `UniTask` continues
synchronously**, the same way any `async` method runs synchronously up to its first real
suspension point. `FakeWindowsManager.OpenAsync` returns an already-completed `UniTask<WindowHandle>`,
so calling (not awaiting) `runner.ShowAvailableWindowsAsync()` has already driven every `OpenAsync`
call it's going to make before hitting a real suspension point (`handle.WaitForCloseAsync()` on a
window nobody has closed yet) — tests can assert on `FakeWindowsManager.OpenCalls` immediately,
with no polling. Only genuinely time-driven behavior (the runner's 250 ms interrupt poll) needs a
bounded wait loop — see `WaitUntilAsync` in `WindowQueueRunnerTests`.

## Fakes (`Tests/Editor/Fakes/`)

| Fake | Stands in for | Notes |
|---|---|---|
| `FakeWindowQueueAggregator` | Any real `IWindowQueueAggregator` | Tests flip `Available`/`Payload` directly, mid-test, to simulate "the reward just became claimable" or "the offer just expired" without driving a real game-state manager. |
| `FakeWindowsManager` | `IWindowsManager` | Records every `OpenAsync` call (`OpenCalls`), hands back a **real** `WindowHandle` wired to a close callback that flips `IsQueueIdle` back to `true` *before* calling `MarkClosed()` — the ordering matters and mirrors production (`WindowsManager` always removes an instance from its tracking before `MarkClosed()`), avoiding a re-entrancy race where the runner's resumed continuation reads a stale `IsQueueIdle`. `CloseCurrentWindowAsync`/`CloseTopPopupAsync` are no-ops — the runner only ever closes through the `WindowHandle` it was given. |

`FakeWindowHandle` access to `internal` members (`SetState`/`MarkClosed`) is granted via
`[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]` in `Assets/Scripts/AssemblyInfo.cs` —
see that file's own comment for *why* it targets `Assembly-CSharp-Editor` specifically (the
"magic Editor folder" convention) rather than a custom `.asmdef` test assembly.

## Coverage map

**`WindowQueueManagerTests.cs`** (pure, synchronous `[Test]`s):
priority-descending/`WindowType`-ascending-tiebreak ordering; `SetItems(null)` clears; cooldown
gating (never-shown → ready; just-shown with positive cooldown → not ready; just-shown with zero
cooldown → ready; cooldown is per-`WindowType`); `SetItems` called again resets cooldown tracking
*and* drops types no longer present.

**`WindowQueueRunnerTests.cs`** (`[UnityTest]`s via `ToCoroutine()`):
opens the highest-priority *available* window (not simply the highest-priority *configured* one)
and completes once it closes; does nothing while the queue isn't idle; skips an aggregator that
reports unavailable; skips a queued item with no registered aggregator (rather than throwing or
hanging); a concurrent second call while a burst is in flight is a same-frame no-op
(re-entrancy guard); a non-interruptible window is never force-closed by a higher-priority
arrival; an interruptible window *is* force-closed (reaching `WindowLifecycleState.Disposed`,
cooldown left untouched) the moment a strictly-higher-priority window becomes available, the
interrupting window opens immediately after, and the interrupted window is reconsidered (as a
brand-new `WindowHandle`) once the interrupting one closes.

## Extending this

If you touch `WindowQueueManager` or `WindowQueueRunner`, add a test here before/alongside the
change — this is the one place in the repo where "evidence the core logic is verified" is expected
by design, and the fakes already make that cheap to do. If you add a third
`IWindowQueueAggregator` in the demo config, consider adding a runner test with three items to
cover a tie or a chain of interrupts, since today's tests only ever exercise two.

Run via Unity's **Window → General → Test Runner → EditMode → Run All** (or `Run Selected` on an
individual fixture/test).
