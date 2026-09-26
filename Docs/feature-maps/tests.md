# Feature map — Tests

**Folders:** `Assets/Tests/EditMode/**` (assembly `PopupSystem.Tests.EditMode`),
`Assets/Tests/PlayMode/**` (assembly `PopupSystem.Tests.PlayMode`)
**Covers:** `Game.Services.WindowQueue.WindowQueueManager`,
`Game.Services.WindowQueue.WindowQueueRunner`, `Game.Services.DailyReward.DailyRewardManager`,
`Game.Services.Time.ServerSyncedTimeProvider`, `Game.Services.Offer.OfferManager`, the slot
inventory (`StackPacker`, `InventoryManager`, `InventoryHasher`, `InventorySyncService`,
`RewardGrantService`, `FileInventoryStorage`), `Game.Services.Wallet.WalletManager`,
`UI.Runtime.Manager.WindowsManager`, `UI.Runtime.Factory.WindowFactory`,
`UI.Services.RemoteImageLoader`, the concrete window controllers' own flows, and a boot smoke of
the real `MainScene`
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
| Collaborators | fakes | only the backend, the clock, the image loader and inventory storage are faked |
| Needs | nothing — no scene, no container, no prefabs | a canvas, a DI container, real Addressable prefabs, real frames |

The dividing line is not "fast tests here, slow tests there". It is what the subject actually
needs. The queue is plain C# that talks to an interface, so faking that interface costs nothing and
buys millisecond tests. The window engine instantiates prefabs, parents them under a canvas and
animates them over real frames — faking any of that would mean testing the fake. And a *view* is
only a valid object when it comes from its own prefab, which is what pushes the controller-flow
fixture into PlayMode too.

Note what the EditMode assembly does **not** reference: `PopupSystem.UI`. The queue is testable
without the window engine at all, which is the layering claim in `Docs/architecture.md` §3 stated as a build
fact rather than a convention. Note also what follows from it: `PopupSystem.Tests.EditMode` is an
Editor-only assembly, so the PlayMode suite cannot reuse its fakes.

## EditMode

All fixtures are plain `async Task` or synchronous `[Test]`, NUnit constraint model throughout.

| Fixture | Subject |
|---|---|
| `WindowQueueManagerTests` | Priority ordering, cooldown gating and expiry, the `SetItems`/`MarkShown` bookkeeping that drives them, and `TimeUntilCooldownReady` (a different question from `IsCooldownReady`, and the one the runner's pacing depends on). |
| `WindowQueueRunnerTests` | The runner's behaviour against fakes: selection, idle-gating, interrupts, suppression, re-arming, re-entrancy, config refresh, scheduled wakes, failure retry backoff, aggregator isolation, and the idle monitor surviving a failing source. |
| `DailyRewardManagerTests` | The availability contract `DailyRewardWindowAggregator` reads, plus the all-or-nothing property of a cancelled claim, and where a claim lands (currencies in the wallet, items in the inventory, refused without starting the cooldown when the inventory is full), and the one-grant-per-interval guard (a claim on cooldown or a second concurrent claim is refused; a cancelled claim releases the guard); a throwing inventory or availability observer cannot abort the claim or leave it repeatable, and inventory observers already see the reward as claimed. Worth its own fixture because the aggregator is a two-line adapter: every decision the queue makes about the daily reward is really made here. |
| `ServerSyncedTimeProviderTests` | The production `ITimeProvider`. Every cooldown, the daily reward and the offer's active window read this one class, so "does it actually stop reading the device clock" is the single assertion the rest of the queue's correctness rests on. |
| `StackPackerTests` | The stacking arithmetic in isolation: top up partial stacks first, then open new ones of `MaxStack`; remove from the smallest stack first; slot need summed over a whole bundle before anything moves; sums near `int.MaxValue` saturate rather than wrap. |
| `InventoryManagerTests` | The rules around the packer: `TryAdd` is all-or-nothing across a bundle, an unknown item is a result not an exception, a lowered limit blocks adding without discarding, every mutation raises `Changed` once, a throwing `Changed` observer cannot fail the add or skip its save, the per-item unit limit (`QuantityLimitExceeded`, counting what is held and summing duplicate lines), saves are serialised — mutations during a held save are written once, as the newest snapshot — and a failed save is logged and swallowed (`LogAssert.Expect`). |
| `InventoryHasherTests` | The canonical string and two fixed SHA-256 vectors. These are a contract with the backend: a change here is a protocol change. |
| `InventorySyncTests` | `InventorySyncService` against `FakeInventoryStorage` + `FakeInventoryRpcManager`: the local cache is loaded before the network call and never re-written by `Load`, `Valid` keeps it, `Replace` overwrites it, a corrupt file is logged and treated as empty, a failed sync fails the connect. |
| `RewardGrantServiceTests` | The local transaction: a refused bundle, an unaffordable price or a credit past the balance limit moves nothing; `CanGrant` agrees with what `Grant` would do; observers run only once every half is applied, one notification per service; a throwing observer neither aborts the grant, skips the save nor stops the other observers; a re-entrant observer cannot spend between the check and the charge. |
| `WalletManagerTests` | Balance limits (a credit past `MaxBalance` refused and nothing changed, including the other credits of the same call and two credits that only overflow together; exactly `MaxBalance` accepted), a negative credit as a programming error, an overdrawn `Spend` refused, and a throwing observer unable to fail a spend that already happened. |
| `OfferManagerTests` | The offer has a price and is not free; a purchase debits the price and grants the bundle; `CanAfford` follows the balance; a purchase without the funds, or into a full inventory, throws and moves nothing; an expired offer is refused; a second concurrent purchase is refused and only one bundle moves; funds that go during the round-trip are caught by the re-check; a cancelled purchase releases the guard; a throwing inventory observer cannot leave the bundle unpaid. |

`WindowQueueRunnerTests` has a `[TearDown]` that disposes the runner. That is not tidiness: the
runner subscribes to every aggregator and to `IWindowsManager.QueueBecameIdle` in its constructor,
so a leaked one would sit there reacting to the *next* test's fakes.

### Fakes (`Tests/EditMode/Fakes/`)

| Fake | Stands in for | Notes |
|---|---|---|
| `FakeTimeProvider` | `ITimeProvider` | A clock the test moves by hand, starting at a fixed arbitrary instant so a failure reproduces identically tomorrow. This is what makes cooldown *expiry* testable at all. |
| `FakeClockRpcManager` | `IRpcManager` | The smallest manager that satisfies `ServerSyncedTimeProvider`, which only calls `Core.GetServerTimeUtcAsync`. The other four modules are left **null on purpose**: a test that starts reaching through this fake should fail loudly, not quietly succeed on invented data. |
| `FakeWindowQueueAggregator` | Any real `IWindowQueueAggregator` | `ThrowFromIsAvailable` / `ThrowFromCreatePayload` / `ThrowFromNextAvailabilityChange` make the matching member throw, and `IsAvailableCalls` counts calls, for the runner's aggregator-isolation and backoff tests. Can take **either of the two shapes** the real ones have, because the runner treats them completely differently. *Event-shaped* (like `DailyRewardWindowAggregator`): set `Available`, and the setter raises `AvailabilityChanged`. *Schedule-shaped* (like `OfferWindowAggregator`): set `AvailableFromUtc`, and availability flips because the injected clock passed a timestamp with **nothing raised at all** — `NextAvailabilityChangeUtc` is then the only thing that can wake the runner. Setting both would model neither, so `AvailableFromUtc` wins. |
| `FakeInventoryStorage` | `IInventoryStorage` | In-memory, with `ThrowOnSave`/`ThrowOnLoad` switches so the save-failure and corrupt-file paths are a fact about the test rather than a real broken disk. `HoldSaves` keeps every write in flight until `ReleaseNextSave()` (whose continuation — the manager's save loop — runs inside that call); `HeldSaveCount` and `SaveRequests` (every snapshot handed to `SaveAsync`, in order) let a test check save ordering against a real in-flight write. |
| `FakeInventoryRpcManager` | `IRpcManager` | Only `Inventory` (scripted per test through `SyncAnswer`: `Valid`, `Replace(snapshot)`, throw) and `RemoteConfig` (the demo inventory config) are implemented. Core, Wallet and WindowQueue throw `NotSupportedException`, so a test that reaches them fails loudly. |
| `TestItems` | — | The catalog (`sword` ×1, `potion` ×10, `arrows` ×50) and bundle builders the packer, manager and hasher fixtures share, so a test reads as "5 slots, add 15 potions" rather than as constructor calls. |
| `TestGrants` | — | A wallet + inventory + grant service wired on fakes the way `AppInstaller` wires them, on the production catalog; used by the daily-reward, grant and offer fixtures. |
| `ProductionCatalog` | — | The catalog `FakeRemoteConfigApi` serves, because the daily reward and the offer name production item ids. |
| `FakeWindowsManager` | `IWindowsManager` | Records every `OpenAsync` (`OpenCalls`) and hands back a **real** `WindowHandle` wired to a close callback, so a test drives "the player closed the window" — or the runner force-closing it — with `handle.CloseAsync()`. `OpenExceptionFor` makes one window type fail to open, which is the way into the runner's failure retry backoff; like the real manager, a failed open sets `IsQueueIdle` and raises `QueueBecameIdle` before throwing. `OpenAttempts` counts every call, failed ones included. `CloseCurrentWindowAsync`/`CloseTopPopupAsync` are no-ops: the runner always closes through the handle. |

Two things about that last row are load-bearing and easy to break. The close callback flips
`IsQueueIdle` **before** calling `MarkClosed()`, mirroring production; and `OpenExceptionFor`
*throws* rather than returning a faulted task, because a real window blowing up in its own
construction does so synchronously too, before `OpenAsync` ever yields — after raising the same
`QueueBecameIdle` the real manager raises on a failed load, which is the wake the runner's backoff
must not turn into an immediate retry. Both, and the
fake-can-drift-from-production problem in general, are in
[`testing-in-unity.md`](../knowledge-base/testing-in-unity.md).

`FakeWindowQueueAggregator`'s schedule-shaped mode is the reason the fake needs a clock at all.
With `NextAvailabilityChangeUtc` hardcoded to null, `WindowQueueRunner.NextScheduledWakeMs`
collapses to the 60-second fallback heartbeat in **every single test** — so the scheduled-wake half
of the runner is executed by nothing.

Access to `WindowHandle`'s `internal` members is granted by
`[assembly: InternalsVisibleTo("PopupSystem.Tests.EditMode")]` in
`Assets/Scripts/Contracts/AssemblyInfo.cs`, which names this assembly and `PopupSystem.UI`
explicitly rather than opening every internal to the whole editor-default assembly. The PlayMode
assembly gets `PopupSystem.UI`'s internals the same way, from `Assets/Scripts/UI/AssemblyInfo.cs`.

## PlayMode

Everything above the `Game` services is real: real `WindowsManager`, real
`WindowFactory`, real prefabs by Addressables address, real transitions over real frames. Only the
backend, the clock, inventory storage and (in the flow fixture) the image loader are faked, because
those are the seams whose behaviour a test needs to *choose* — a failing endpoint, a banner that
never arrives, an offer window that has closed.

| Fixture | Subject |
|---|---|
| `WindowsManagerPlayModeTests` | The engine itself. Because the provider is the real one, this is also the only automated check that the addresses in the `WindowDefinition`s actually resolve — and the honest answer to the fake-drift criticism of the EditMode suite: nothing here is faked, so nothing here can drift. |
| `WindowFlowPlayModeTests` | The concrete window controllers' flows: claim, purchase, remote content, the inventory grid's scrolling, and what each does when the thing it is waiting for fails or arrives too late. These flows are where runtime defects are most likely. |
| `RemoteImageLoaderPlayModeTests` | The real `RemoteImageLoader` against the real StreamingAssets file the demo serves its offer banner from — the same "remote" endpoint `AppInstaller` wires up. PlayMode because `UnityWebRequest` needs the player loop, and because the point is that the download actually happens. |
| `MainSceneBootPlayModeTests` | Boot smoke of the real `MainScene`: loads it additively, waits for the main screen, checks the preloader is gone and the scene's `EventSystem` has an input module, lets the startup queue settle and unloads. It is the only fixture that exercises `SceneContext`, `AppInstaller`, the scene's serialized references and the startup state machine; any error or exception logged during boot fails it. It does not drive input. The one binding it replaces is `IInventoryStorage`: the same `FileInventoryStorage`, re-bound through `SceneContext.AfterInstallHooks` onto a scratch directory, so the boot's starter-kit save lands there (asserted) and the developer's save under `persistentDataPath` is asserted untouched. |
| `FileInventoryStoragePlayModeTests` | The real `FileInventoryStorage` against the real file system, rooted in a random directory under `Application.temporaryCachePath` so it can never touch a developer's save. Round trip with the hash, atomic overwrite leaving no `.tmp`, a corrupt file and a tampered file both rejected. Its exceptions are captured with an `await` in the body — the `Throws` constraint around `LoadAsync` deadlocked the editor (`testing-in-unity.md`). |

`TestUiHierarchy` is the shared world: a code-built copy of `MainScene`'s `UIRoot`, implementing
`IUILayerProvider`. Why it is built rather than loaded, why it carries no `EventSystem`, and the
rest of the fixture setup rules (the preload precondition, idempotent teardown, typing
`WindowsManager` as the concrete class) are in
[`testing-in-unity.md`](../knowledge-base/testing-in-unity.md).

The window fixtures bind `InMemoryInventoryStorage` (`Tests/PlayMode/`) rather than the file
storage: the windows under test never need a file, and the real storage would write under the
developer's `persistentDataPath`. It is a second in-memory fake rather than a reuse of the EditMode
one because the EditMode assembly is editor-only (`zenject-composition.md`).

`WindowsManagerPlayModeTests` keeps its container small on purpose — `Settings` (a plain `Window`,
non-modal, `WindowsLayer`, with a transition), `RewardPopup` (a `Popup`, modal, close-on-backdrop,
`PopupsLayer`) and `MainGame` (the base screen) cover every interesting shape.

## Coverage map

The map below covers the queue, clock and window fixtures; the inventory, offer and file-storage
fixtures are summarised in the fixture tables above.

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
opening is skipped until its retry time **without** advancing its cooldown or its "had its turn"
state, and the burst falls through to the next item; the idle monitor does not hammer a window that
keeps failing to open (the fake's `QueueBecameIdle` wake notwithstanding); a throwing aggregator
(`IsAvailable`, `CreatePayload` or the schedule) takes only its own window out while the rest keep
being served and the monitor keeps running; a window dropped from the config and brought back loses
its already-shown state, while a refresh that still contains a window keeps suppressing it; and a
config arriving while the runner is parked on its longest sleep wakes it.

**`DailyRewardManagerTests`** — available from the very first frame (`NextAvailableAtUtc` starts at
`MinValue`, not "now", because the constructor runs before the clock is synced); a claim makes it
unavailable and raises `AvailabilityChanged`; it comes back once the interval elapses, with nothing
raised at that moment — which is exactly why the aggregator reports `NextAvailableAtUtc` instead; a
cancelled claim leaves availability untouched (all-or-nothing); a claim puts the currencies in the
wallet and the items in the inventory; `CanClaimIntoInventory` is false when the inventory is full,
and a claim into a full inventory fails without starting the cooldown; a second claim during the
cooldown, or a second claim started before the first resolved, is refused and moves nothing (the
window's flag only ever protected one instance); a claim cancelled mid-flight leaves the reward
claimable and the guard released; `TimeUntilAvailable` is zero while available, counts the cooldown
down with the clock and never goes negative.

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
`StateChanged` announces `Closing` before `Disposed` without skipping either (nothing in production
subscribes to `WindowHandle.StateChanged`, so this test is the only check on the lifecycle);
the base screen hands back the same handle when opened again while up and never counts as queue-busy,
even after being closed and reopened; `Dispose` tears down everything still on screen, releasing
every controller and completing every handle anything was parked on; disposing the factory
destroys what the pool was holding; a window counts as queue-busy from the moment `OpenAsync` is
called, *before* its prefab has loaded (`ScriptedPrefabProvider` holds the real load for a frame — the
EditMode fake is busy here by construction, so only this test holds production to it, see
`testing-in-unity.md`); an open whose prefab load throws surfaces the exception, leaves the queue idle
and raises `QueueBecameIdle` so a sleeping runner is not stranded until its heartbeat; the
`MainGame` Settings button comes back after a failed open instead of staying dead for the session;
an open whose prefab finishes loading after `Dispose()` ends as a cancellation and never shows its
view; an `OpenAsync` after `Dispose()` is refused without loading anything; a throwing `Closing` or
`Disposed` observer, or a throwing controller `Dispose`, still completes the close (and `Dispose`
still tears down every window); two concurrent base-screen opens during a prefab load create one
instance, and when that load fails both see the failure and a retry works; and a controller
creation that throws hands a fresh view back hidden, or a pooled one back to the pool (see
[`window-core.md`](window-core.md)).

**`WindowFlowPlayModeTests`** — the claim opens the reward popup *while the claim is still in
flight* (which is what makes the popup's loading state a real signal), and the popup shows one icon
per reward line; a `DailyReward` window
reopened from the pool has its claim button enabled again (invisible on the first open, bricks every
one after — it advances the clock past the cooldown first, because a reopened window on cooldown
shows its timer instead); a `DailyReward` window on cooldown shows the timer in place of the button,
counts down with the clock and gives the button back on its own when the cooldown ends; when the
reward popup fails to open (`ScriptedPrefabProvider` fails its load), a claim or
a purchase that went through is granted and charged exactly once and closes its window, and a
purchase that failed as well gives the Buy button back; the inventory grid scrolls from anywhere in its viewport, keeps moving after a fling
is released, and overscrolls past the top then springs back; the offer opens with a placeholder
and then fills in the remote copy; its Buy button is disabled while the wallet cannot cover the
price and re-enabled once it can; it falls back to
safe copy when the remote-config endpoint fails, and stays *usable* because the terms are local;
it reports no active offer once its window has closed (a branch that becomes unreachable if
`OfferManager` derives both ends of the window from "now"); an offer closed while its
content is still in flight never writes into the torn-down view; and the reward popup shows an error
when the operation it observes *fails* but says nothing when that operation is *cancelled*.

**`RemoteImageLoaderPlayModeTests`** — the same texture comes back for a repeated URL (without the
cache, every reopen is a fresh download and a fresh allocation); `Dispose` destroys the cached
textures, which is the *only* place they are destroyed (if this regresses to "nobody destroys
them", every banner ever downloaded outlives the session); and a missing address throws, so the
caller can degrade to the no-banner layout rather than being handed a null texture as though it had
worked.

## Extending this

If you touch `WindowQueueManager` or `WindowQueueRunner`, add an EditMode test; if you touch
`WindowsManager`, `WindowFactory` or a window controller's flow, add a PlayMode one. Both fixtures
already make that cheap.

Gaps worth closing next, in rough order of value:

- **Real input in the real scene.** `MainSceneBootPlayModeTests` proves the scene boots, but
  nothing clicks through its `EventSystem` or checks modal input blocking there.
- **Three queue items.** Today's runner tests only ever exercise two, so a tie or a chain of
  interrupts is untested.
- **The fallback heartbeat.** By construction the one path no test exercises, and exactly what would
  mask a future aggregator that forgets to raise `AvailabilityChanged`.

Run them from Unity's **Window → General → Test Runner** (EditMode and PlayMode tabs), from
`Tools/Tests/Run EditMode|PlayMode tests (write results)` — which writes the outcome to
`Claude outputs/TestResults/<mode>.txt` for an agent that has no Test Runner window
([`testing-in-unity.md`](../knowledge-base/testing-in-unity.md)) — or let CI do it — see `Docs/ci/`.
