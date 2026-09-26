# PopupSystem — architecture and technical guide

This is the technical guide for engineers and agents working in this repository: the project,
its architecture, extension workflow, tests and known limitations. Agent working rules live in
[AGENTS.md](../AGENTS.md); module details and technical reasoning live in the documents linked below.
All paths shown in code spans are relative to the repository root.

> **Project stage: prototype / take-home assessment.** This codebase was built to answer a Unity
> technical assessment (see `UnityDevTest.md`, if present, for the original brief). It is
> deliberately scoped to demonstrate architecture, not to ship. See "Prototype status & known
> gaps" near the end of this file before treating anything here as production-ready.

## 1. What this project is

A Unity "Popup System": a generic engine for opening, sequencing, and closing UI windows/popups
with priorities, cooldowns, interrupts, a forward-only lifecycle, pooled views, remote-sourced content,
and modal backdrop handling — plus six concrete windows (`MainGame`, `Settings`, `Inventory`, `DailyReward`,
`Offer`, `RewardPopup`) built on top of it as a demonstration.

The demo scene is `Assets/Scenes/MainScene.unity`. Running it drives: app bootstrap → simulated
server connect (with retry-on-failure) → main game screen → an automatic queue that opens
`DailyReward` (local, high priority, non-interruptible) and `Offer` (remote-config-sourced content
+ remote image, lower priority, interruptible) according to priority/cooldown/availability, while
`Settings`, `Inventory` and `RewardPopup` are opened on demand by user action.

## 2. Tech stack

- **Engine:** Unity `6000.3.12f1` (Unity 6), Universal Render Pipeline, UGUI (`com.unity.ugui`),
  TextMeshPro for every piece of text (legacy `UnityEngine.UI.Text` appears nowhere).
- **DI container:** [Zenject](https://github.com/modest-tree-games/zenject) — vendored as source
  under `Assets/Zenject` (not a package), bound in `AppInstaller`. It has its own
  `Zenject`/`Zenject.Editor` assemblies, so editing a game script does not recompile it.
- **Async:** [UniTask](https://github.com/Cysharp/UniTask) (`com.cysharp.unitask` git package)
  everywhere instead of coroutines or bare `Task`. Every async API in the project returns
  `UniTask`/`UniTask<T>` and takes a `CancellationToken`.
- **Content loading:** Addressables (`com.unity.addressables`). Every UI prefab lives under
  `Assets/Content/UI/**`, is marked Addressable in the `UI` group, and is loaded through
  `IUiPrefabProvider`. The only `Resources` folder is TextMesh Pro's own
  (`Assets/TextMesh Pro/Resources`); nothing of ours loads through `Resources`.
- **Networking (demo):** `UnityWebRequest` via `RemoteImageLoader`, pointed at
  `Application.streamingAssetsPath` in this build (see §7).
- **Tests:** Unity Test Framework (`com.unity.test-framework` 1.6), NUnit constraint model, plain
  `async Task` bodies. Two suites: `Assets/Tests/EditMode` (the queue, against fakes) and
  `Assets/Tests/PlayMode` (the window engine, against the real thing) — see §9.

There is no backend and no build pipeline. CI is written but not yet wired up — see §9 and §10.

## 3. Layering & dependency direction

The code is organized into layers, and the dependency arrow only ever points one way. **Each layer
is its own assembly definition, so the direction is enforced by the compiler rather than by
convention** — a `using` that points the wrong way is a build error, not a code-review finding:

```
App  ──────────────┐        (bootstrap, app-level state machine, the single composition root)
  |                |
  v                |
UI   ──────────┐   |        (window engine + concrete window Views/Controllers/Modules)
  |            |   |
  v            |   |
Game ────┐     |   |        (domain models + services — the "business logic", UI-agnostic)
         |     |   |
         v     v   v
     Contracts             (the presentation port — see below)

Core   (Extensions/ — UniTask helpers; referenced by UI, depends on nothing of ours)
```

| Folder | Assembly | References (ours) |
|---|---|---|
| `Assets/Scripts/Contracts` | `PopupSystem.Contracts` | — |
| `Assets/Scripts/Extensions` | `PopupSystem.Core` | — |
| `Assets/Scripts/Game` | `PopupSystem.Game` | `Contracts` |
| `Assets/Scripts/UI` | `PopupSystem.UI` | `Contracts`, `Core`, `Game` |
| `Assets/Scripts/App` | `PopupSystem.App` | `Contracts`, `Game`, `UI` |
| `Assets/Tests/EditMode` | `PopupSystem.Tests.EditMode` | `Contracts`, `Game` — deliberately *not* `UI` |
| `Assets/Tests/PlayMode` | `PopupSystem.Tests.PlayMode` | `Contracts`, `Core`, `Game`, `UI` |
| `Assets/Editor/UiKit` | `PopupSystem.EditorTools` | `UI` (editor-only) |
| `Assets/Editor` (root) | `PopupSystem.EditorTools.TestRunner` | — (editor-only; `TestRunnerApi` menu + result probe) |
| `Assets/Zenject` | `Zenject` / `Zenject.Editor` | — |

- **`Contracts` is the presentation port, not a dumping ground.** It holds exactly what the queue
  (which lives in `Game`) needs in order to ask for a window without knowing anything about how
  windows are built: `WindowType`, `WindowLifecycleState`, `IWindowData`, `EmptyWindowData`,
  `IWindowsManager`, `WindowHandle`. Everything else about the window engine — `WindowDefinition`,
  `IWindowModule`, `WindowView`, `WindowFactory`, `WindowsManager` itself — stays in `UI`. Before
  adding a type here, check whether `Game` actually needs it; if only `UI` does, it belongs in `UI`.
- **`Game` never references `UI`.** The assembly definitions enforce it: `PopupSystem.Game`
  references `PopupSystem.Contracts` and nothing else of ours. A type `Game` needs from the window
  engine goes into `Contracts` as a port, never as a `PopupSystem.UI.*` import.
- **`UI` may reference `Game`** — that is the allowed direction, and window controllers use it
  (`OfferWindowController` → `OfferManager`, `RewardPopupRequest` → `RewardPopupData`). Keep a
  window's payload type next to that window under `UI/Windows/<Feature>/`, never under
  `Game/Domain`.
- **`App` wires `Game` and `UI` together** via `AppInstaller` (the single Zenject composition
  root) and drives the app-level state machine (`AppInitState` → `AppConnectServerState` →
  `AppMainGameState`). It is the only assembly that sees both.
- Everything is bound through interfaces (`IWindowsManager`, `IRpcManager`, `IWindowModule`,
  `IWindowQueueAggregator`, `IWindowTransition`, `IRemoteImageLoader`, `IUILayerProvider`,
  `ITimeProvider`, …) and constructor-injected by Zenject. There is no
  `MonoBehaviour.Find`/singleton-locator pattern anywhere in the engine, and the engine depends on
  no concrete `MonoBehaviour` — `WindowFactory` takes `IUILayerProvider`, which `UIRoot`
  implements, so a test can supply layers without a scene.
- **Internals are granted by name, not by blanket.** `WindowHandle.SetState()/MarkClosed()` are
  `internal` to `PopupSystem.Contracts`; `Assets/Scripts/Contracts/AssemblyInfo.cs` names the two
  assemblies allowed to see them (`PopupSystem.UI`, `PopupSystem.Tests.EditMode`). Likewise
  `Assets/Scripts/UI/AssemblyInfo.cs` grants `PopupSystem.Tests.PlayMode` alone access to the
  views' internal buttons and pool hooks.

## 4. The window engine, in one paragraph

A window type is described once by a `WindowDefinition` (layer, modality, prefab *address*,
transition, whether backdrop-tap closes it) inside a small `IWindowModule` implementation that
also knows how to build that window's `IWindowController` — through an injected
`IFactory<TController>`, not an injected `DiContainer`. Every module is multi-bound in
`AppInstaller`; `WindowRegistry` and `WindowControllerResolver` are **fully generic** — they only
ever iterate the bound modules, so adding a new window type never means editing either of them.
`WindowFactory.CreateAsync` loads the prefab through `IUiPrefabProvider` (Addressables),
instantiates it (or pops one from a pool) and hands back a
`WindowInstance`; `WindowsManager` owns the actual open/close orchestration, the three logical
stacks (base window, window stack, popup stack), and delegates backdrop compositing to
`ModalBackdropPresenter`. A window's controller only ever sees its own typed payload
(`WindowController<TData, TView>`) and drives a forward-only `WindowLifecycleState` machine
(`Initializing → Opening → Active → Closing → Disposed`) exposed on `WindowHandle`:
`SetState` ignores a repeat or a backward move, and allows a stage to be skipped.

On the scene side, each `UIRoot` layer is its own `Canvas` + `GraphicRaycaster` (sorting bands
1000/2000/3000/4000) and so is every window prefab, so animating one window rebuilds only that
window rather than every graphic on screen. Sibling index is still what decides stacking;
`UILayerSorter` turns it into the `sortingOrder` that separate canvases need in order to both draw
and *raycast* in that order. Two traps live there — a nested canvas without its own raycaster
receives no input at all, and `Canvas.overrideSorting` is ignored while the GameObject is inactive
— both measured, both written up in
`Docs/knowledge-base/unity-ui-canvas-and-input.md`.

Full detail: `Docs/feature-maps/window-core.md`.

## 5. The priority queue, in one paragraph

`WindowQueueManager` holds the current set of queueable window types (priority + cooldown +
whether they can be interrupted), refreshed from `IRpcManager.WindowQueue` at connect time.
`WindowQueueRunner` is the thing that actually *acts* on it: while `IWindowsManager.IsQueueIdle`,
it asks each bound `IWindowQueueAggregator` (one per queueable feature — `DailyReward`, `Offer`)
whether its window is currently available, picks the highest-priority available+off-cooldown one,
opens it, and either waits for it to close naturally or — if it opted into
`AllowInterrupt` — races that wait against a watcher for a strictly-higher-priority window
becoming available, force-closing and re-queuing it if the watcher wins. A per-burst "already
shown" set prevents starvation of lower-priority items by a zero-cooldown one that's still
technically "available". Every aggregator call is contained to its own window: a window whose
open fails or whose aggregator throws is logged and left out until a retry time that backs off
exponentially on the injected clock, so one broken source cannot stall or spin the queue.

**The runner sleeps; it does not poll.** The 500 ms idle tick and the 250 ms interrupt tick are
both gone. It wakes on `IWindowQueueAggregator.AvailabilityChanged`, on
`IWindowsManager.QueueBecameIdle`, or at the earliest moment anything can change on its own — the
next cooldown expiry or `IWindowQueueAggregator.NextAvailabilityChangeUtc`, computed from the
config. A 60-second fallback heartbeat exists purely as a safety net for a future aggregator that
forgets to raise its event; nothing is supposed to depend on it.

Full detail: `Docs/feature-maps/window-queue.md`.

## 6. Adding a new popup/window type (the intended workflow)

This is the extension point the assessment specifically asks for ("another developer [can] add
new popup types... without modifying the core engine"). Steps:

1. Create `Assets/Scripts/UI/Windows/<Feature>/` with three files following the existing windows
   as a template:
   - `<Feature>View.cs` — `sealed class ... : WindowView`, `[SerializeField]` references to its
     own prefab's UI parts, raises `RequestClose()` from a close button, exposes plain setter
     methods (`SetContent`, etc.) and any user-action events the controller subscribes to.
   - `<Feature>Controller.cs` — `sealed class ... : WindowController<TData, TView>`, injects
     whatever `Game` services it needs, implements `OnInitializeAsync` and (if it subscribed to
     anything) overrides `Dispose()` to unsubscribe.
   - The prefab's **root** needs a `Canvas`, a `GraphicRaycaster` and a `CanvasGroup` (the prefabs
     leave `overrideSorting` off; `UILayerSorter` turns it on when the window opens) — copy an existing window prefab rather than building the root by hand. Miss
     the raycaster and the window renders but ignores every click; miss the `Canvas` and it draws
     underneath every window that has one.
   - `<Feature>Module.cs` — `sealed class ... : IWindowModule`, builds the `WindowDefinition`
     (pick a `WindowType`, `UIEntryKind`, `UILayerType`, modality, prefab **address**,
     transition, `closeOnBackdropClick`, `isBaseScreen`) and implements `CreateController()` via an
     injected `IFactory<...Controller>` — never an injected `DiContainer`. `isBaseScreen` is the
     one flag a normal window never sets: it marks the persistent screen everything else opens on
     top of (`MainGame`), which is single-instance, lives in its own slot rather than on the window
     stack, and does not count as "the screen is busy" for the queue. Exactly one window type
     should have it.
   - If the window needs typed input, add an `IWindowData` implementation next to it (see
     `EmptyWindowData` for "no payload", `RewardPopupRequest` for "payload is an in-flight
     operation the window observes"). The payload type itself lives with the window in `UI`; only
     the `IWindowData` marker it implements comes from `Contracts`.
2. Add the new value to the `WindowType` enum (`Assets/Scripts/Contracts/WindowType.cs` — it is in
   `Contracts` because the queue, which lives in `Game`, selects windows by type).
3. Build the content prefab under `Assets/Content/UI/Windows/<Feature>Window.prefab`, mark it
   Addressable in the `UI` group, and give it the address `UI/Windows/<Feature>Window` — that
   address is what goes in the `WindowDefinition`. There is no code-built fallback: an address that
   does not resolve is a hard `InvalidOperationException`.
   - **Style it from the kit, do not invent one.** Every visual in this project comes from the
     Lagoon Gold set: the sprites in `Assets/Content/UI/Sprites/` (panel, card, chip, pill button,
     round button, icons), the palette tokens, and the layout rules — panel sized explicitly, no
     `LayoutGroup`, `Image.color = white` with the state carried by `Button.colors`, the close
     button at `(1, 1)` with the measured numbers. `Docs/feature-maps/ui-atlas.md` is the
     reference, `Docs/mockups/` holds the mockups as PNGs (exported from the "PopupSystem UI Kit"
     design canvas, which stays the source of truth). A new sprite means
     editing `Tools/ui-atlas/generate_sprites.py` and re-running it, never a hand-painted PNG
     dropped into the folder.
   - After touching sprites or fonts, run `Tools/UI Kit/Configure sprites and build atlas` and
     `Tools/UI Kit/Build fonts and apply`. The first one forces `spriteMode = Single`, which
     renames sprite sub-assets and silently breaks any `Image` still pointing at the old name — a
     white quad, no error. Re-render and look at the result before calling it done: select the
     prefab and run `Tools/UI Kit/Render prefab`, which writes a 1080x1920 PNG into
     `Claude outputs/UIKit/Renders/`. It builds its camera and canvas in a throwaway additive scene and
     closes that scene in a `finally`, so a render that fails half-way cannot leave objects in the
     scene you have open — do not hand-build a render harness in `MainScene` again.
4. Bind the new `IWindowModule` in `AppInstaller.InstallBindings()`, and bind its controller with
   `Container.BindIFactory<...Controller>().To<...Controller>()` next to the other six.
5. If this window should appear automatically via the priority queue (rather than only by a direct
   `IWindowsManager.OpenAsync` call from other UI), add an `IWindowQueueAggregator` for it under
   `Assets/Scripts/Game/Services/WindowQueue/Aggregators/` and bind it too; the backend's
   `WindowQueueInfo` list (currently `FakeWindowQueueRpcApi`) needs an entry with this window's
   priority/cooldown/`AllowInterrupt`.

Nothing else changes. `WindowRegistry`, `WindowControllerResolver`, `WindowFactory`,
`WindowsManager`, and `WindowQueueRunner` are all closed for modification for this workflow.

## 7. Local vs. remote sourcing

- **Local:** every window's *content prefab* ships in the app's Addressables content
  (`Assets/Content/UI/...`, group `UI`, loaded by address through `IUiPrefabProvider`) —
  there is no remote-fetched UI layout in this codebase, matching how live games typically ship
  screen layouts (LiveOps changes copy/config/assets, not the UI hierarchy itself).
  `MainGame`/`Settings`/`DailyReward`/`RewardPopup` also have fully local *content* (English copy
  compiled in, rewards computed client-side by `DailyRewardManager`/`OfferManager`). `Inventory`'s
  copy is local too, but its slot limit and item catalog come from remote config at connect
  (`InventorySyncService`).
- **Remote:** `Offer` demonstrates remote-sourced *content*: `IRemoteConfigApi` (faked by
  `FakeRemoteConfigApi`) supplies title/description/CTA copy plus a banner image reference at
  display time, and `IRemoteImageLoader`/`RemoteImageLoader` downloads that banner over
  `UnityWebRequest`. In this build the "remote" endpoint is `Application.streamingAssetsPath`
  (see `Assets/StreamingAssets/offers/starter-offer-banner.png` and the
  `WithArguments(GetStreamingAssetsBaseUrl())` binding in `AppInstaller`, which prefixes `file://`
  when the path has no scheme) — swapping in a real CDN/CMS means changing that one base URL, not
  any window/controller code.
- **Backend as a whole** is faked end-to-end: `IRpcManager` (`Core`/`Wallet`/`Inventory`/
  `WindowQueue`/`RemoteConfig`) is satisfied by `FakeRpcManager`, whose five sub-fakes simulate latency via
  `UniTask.Delay` so loading states are exercised for real. Swapping in a live backend for the
  existing reads means writing one real implementation of `IRpcManager` (or of individual
  sub-interfaces) and changing one binding in `AppInstaller`; game managers see only the interfaces
  (demo item ids live in `DemoItemIds`), and only the composition root and tests reference the
  fakes. That swap does **not** move purchases or claims to the server: there are no purchase/claim
  RPCs, and both run a local delay followed by a local mutation. A real backend needs those APIs
  plus reconciliation — see `Docs/feature-maps/game-services.md`.

## 8. Coding conventions

These hold for anyone changing the code, human or agent.

- **DI:** constructor injection via Zenject everywhere; no field injection, no service-locator
  calls, no `FindObjectOfType`. The single composition root is `AppInstaller`. **Nothing outside
  `AppInstaller` injects `DiContainer`** — a class that needs to create objects at runtime asks for
  the specific capability by type (`IFactory<TController>` in the window modules,
  `PrefabFactory<WindowView>` in `WindowFactory`). That distinction is the whole difference between
  a dependency and a service locator, and it is what makes those classes constructible in a test
  without a container.
- **Async:** `UniTask`/`UniTask<T>` + `CancellationToken`, use `UniTaskVoid`/`.Forget()` for
  deliberate fire-and-forget work with error handling; do not add `async void` or coroutines.
- **Sealed by default:** concrete classes are `sealed` unless designed for inheritance
  (`WindowView`, `WindowController<TData, TView>` are the intentional exceptions).
- **Interfaces for everything crossing a layer boundary** (`I...Manager`, `I...Api`,
  `I...Aggregator`, `I...Module`, `I...Transition`, `I...Loader`) so the concrete implementation
  is swappable and mockable.
- **One feature, one folder:** a window's View/Controller/Module/payload type live together under
  `UI/Windows/<Feature>/`; a `Game` feature's manager + domain model live together under
  `Game/Services/<Feature>/` and `Game/Domain/<Feature>/`.
- **Extend via multi-binding, not switch statements:** `IWindowModule` and
  `IWindowQueueAggregator` are both Zenject multi-bindings consumed generically — this is the
  project's one recurring extensibility pattern and new features should follow it rather than add
  a branch to some central `switch`.
- **No ambient clock:** nothing in `Game` reads `DateTime.UtcNow`. Anything time-gated takes
  `ITimeProvider` (`Game/Services/Time/`), which is anchored to server time at connect. Two
  reasons, both load-bearing: a device clock is player-controlled (and cooldowns are a
  monetisation surface), and an ambient clock makes cooldown expiry untestable without sleeping.
  The only legitimate exceptions are inside the provider itself and in the fake backend that
  stands in for the server.
- **Errors degrade, they don't crash the loop:** a single window failing to open, a remote-config
  fetch failing, or a connect attempt failing must never take down `WindowQueueRunner`'s idle
  monitor or leave the player stuck — see `Docs/knowledge-base/resilience.md` for the specific
  reasoning at each site, including the two failures that are deliberately hard instead. Preserve
  this property when touching those files.
- **The code carries almost no comments; the reasoning lives in `Docs/`.** What stays in a source
  file is a short XML-doc on the ports and contracts (what a member means, and any constraint a
  caller has to honour) plus a one-line warning at the handful of call sites where the surrounding
  lines look removable and are not — an ordering constraint, a deliberate
  `CancellationToken.None`, a pooled-view reset. Everything longer than that — the races, the
  measurements, the arguments for a design — belongs in
  `Docs/knowledge-base/` (cross-cutting, engine- and library-level) or in the relevant
  `Docs/feature-maps/` file (module-specific). **Read those before changing the surrounding code,
  and when you add reasoning, add it there rather than inline.** A comment worth more than one line
  is a sign the note belongs in `Docs/`, with at most a pointer left in the code.

## 9. Testing

The split is by what the subject needs, not by speed. The queue is plain C# talking to an
interface, so faking that interface costs nothing; the window engine instantiates prefabs and
animates them over frames, so faking any of it would mean testing the fake.

`Assets/Tests/EditMode/` (assembly `PopupSystem.Tests.EditMode`) — the queue. No scene, no Zenject
container, no prefabs:

- `WindowQueueManagerTests.cs` — pure-logic coverage of priority ordering and cooldown gating,
  expiry included (`FakeTimeProvider` moves the clock; nothing sleeps).
- `WindowQueueRunnerTests.cs` — behavioral coverage of the runner (highest-priority selection,
  idle-gating, interrupt/force-close/reconsideration, non-interruptible immunity, popup-owning
  windows, re-arming, re-entrancy guard, scheduled wakes, failure retry backoff, aggregator
  isolation and the idle monitor surviving a failing source, config refresh) against
  `FakeWindowsManager`/`FakeWindowQueueAggregator`.
- `DailyRewardManagerTests.cs` — the availability contract the queue's aggregator reads, the
  all-or-nothing property of a cancelled claim, and one grant per interval (a claim on cooldown or
  a concurrent second claim is refused).
- `OfferManagerTests.cs` — the paid offer: it has a gold price and is not free, `CanAfford`
  follows the balance (exactly the price is enough), a purchase debits the price and grants the
  bundle, and a purchase without the funds, into a full inventory, or of an expired offer throws
  without charging or granting anything; a concurrent second purchase is refused, and funds that go
  during the round-trip are caught by the re-check.
- `ServerSyncedTimeProviderTests.cs` — the production `ITimeProvider`: that it stops reading the
  device clock, advances from its anchor, and re-anchors rather than accumulating error.
- `StackPackerTests.cs`, `InventoryManagerTests.cs`, `InventoryHasherTests.cs`,
  `InventorySyncTests.cs`, `RewardGrantServiceTests.cs`, `WalletManagerTests.cs` — the slot
  inventory and the wallet: stacking arithmetic and its overflow limits, the all-or-nothing
  `TryAdd`, save ordering against a held save, the hash the backend has to reproduce (fixed test
  vectors), the connect-time sync against a fake storage and a fake RPC, balance limits, and a
  grant (with or without a price) as one local transaction that throwing or re-entrant observers
  cannot break.

`Assets/Tests/PlayMode/` (assembly `PopupSystem.Tests.PlayMode`) — the half the EditMode suite
structurally cannot reach:

- `WindowsManagerPlayModeTests.cs` — the real `WindowsManager` over the real `WindowFactory`,
  instantiating the real prefabs onto a real canvas through real transitions. Nothing faked:
  layer routing, canvas sorting, view pooling, modal backdrops, the lifecycle, and the
  double-close race. `TestUiHierarchy` builds a code-only copy of `UIRoot` (see
  `Docs/knowledge-base/testing-in-unity.md` for why it builds one rather than loading `MainScene`).
- `WindowFlowPlayModeTests.cs` — the concrete window controllers' own flows: claim, purchase,
  remote content, the pooled-view reset, and what each does when the thing it awaits fails, is
  cancelled, or arrives after the window is gone.
- `RemoteImageLoaderPlayModeTests.cs` — the real loader against the real StreamingAssets endpoint:
  caching, ownership on `Dispose`, and a missing address failing rather than returning null.
- `FileInventoryStoragePlayModeTests.cs` — the real inventory file storage against the real file
  system, in a scratch directory under `temporaryCachePath`: round trip, atomic overwrite, and a
  corrupt or tampered file being rejected rather than trusted.
- `MainSceneBootPlayModeTests.cs` — the boot smoke: loads the real `MainScene` additively and
  checks that the real composition root reaches the main screen with an `EventSystem` and input
  module present and nothing logged as an error. It does not drive input.

Test bodies in both suites are plain `async Task` with NUnit's constraint model
(`Assert.That(x, Is.EqualTo(y))`) — the `[UnityTest] IEnumerator + ToCoroutine()` bridge and the
classic `Assert.AreEqual` are gone.

Run them from Unity's **Window → General → Test Runner** (EditMode and PlayMode tabs), from
`Tools/Tests/Run EditMode|PlayMode tests (write results)` (the outcome lands in
`Claude outputs/TestResults/<mode>.txt`, for an agent without a Test Runner window — see
`Docs/knowledge-base/testing-in-unity.md`), or let CI
do it — the GameCI workflow lives in `Docs/ci/` and still has to be moved into
`.github/workflows/`; that folder's README says why and what secrets it needs. This is exactly
the area the assessment calls out for verification ("evidence that the core logic — especially
the queue and priority system — is verified and reliable"); if you touch `WindowQueueManager` or
`WindowQueueRunner`, extend the EditMode tests; if you touch `WindowsManager` or `WindowFactory`,
extend the PlayMode ones; if you touch a window controller's flow, extend `WindowFlowPlayModeTests`; if you touch
`StackPacker`/`InventoryManager`/`InventoryHasher`, extend the inventory EditMode fixtures — the
hash vectors in `InventoryHasherTests` are a contract with the backend, so a change there is a
protocol change, not a refactor.

## 10. Prototype status & known gaps

This is an assessment build, not a production client. Before building further on it, be aware of:

- **No real backend.** `FakeRpcManager` and its five sub-fakes are the entire "server" — there is
  no HTTP/RPC transport, auth, retry/backoff policy (beyond the one connect-retry loop in
  `AppConnectServerState`), or versioning story.
- **Addressables are wired, but the content pipeline is not.** Prefabs load by address through
  `IUiPrefabProvider`, and `AddressablesUiPrefabProvider` holds every handle it loads for the life
  of the session and releases them on `Dispose`. That is the seam a real pipeline needs, but it is
  only the seam: everything is in one local `UI` group with no remote catalog, no group/bundle
  strategy, no download-on-demand or memory budget, and nothing ever released early.
- **Only two `IWindowQueueAggregator`s exist** (`DailyReward`, `Offer`); `RewardPopup`,
  `Settings` and `Inventory` are opened directly via `IWindowsManager.OpenAsync`, not through the queue, and there
  is no aggregator/test coverage for a *third* concurrent queue source yet.
- **Single scene, no scene-load/unload story**, no localization, no analytics/telemetry hooks.
  Persistence exists for exactly one thing: the slot inventory is cached to a JSON file per player
  and hash-checked against the server at connect (`Docs/knowledge-base/persistence.md`). The
  wallet, the profile and every cooldown are still in-memory and reset on relaunch. The fake
  inventory "server" does not even compare hashes: it accepts any non-empty hash and returns its
  starter kit only for an empty one. A real backend would compare the hash, and still has to
  validate the mutations themselves.
- **Purchases and claims are local transactions.** `RewardGrantService.Grant(reward, price)`
  validates items, balances and price before anything moves and notifies observers only after
  everything is applied, and observers are isolated, so the local state cannot be left half-applied.
  It is still local: there is no purchase/claim RPC, no idempotency and no reconciliation.
- **The inventory has no item effects.** Icons are in (every currency and item is drawn from an
  Addressable sprite — `Docs/feature-maps/ui-atlas.md`, "Reward icons"), but `Use` only decrements
  a stack, and there is no drag-and-drop, split/merge or equipment. `Docs/feature-maps/inventory.md` lists the cut in full.
- **CI exists but is not wired up.** The GameCI workflow is written and sits in `Docs/ci/`; it has
  to be moved to `.github/workflows/` and given a `UNITY_LICENSE` secret before a single run
  happens. No automated build pipeline either. `.editorconfig` is checked in (formatting, the
  naming rules the codebase already follows, and `CA2016`/`CS4014` raised to warnings), but nothing
  enforces it outside the IDE: there is no analyzer package and no format check in CI.
- **The boot fixture is a smoke test only.** `MainSceneBootPlayModeTests` proves the real scene
  and composition root reach the main screen; it does not click through the real `EventSystem` or
  check modal input blocking in the scene. Its one substitution is the inventory storage root (a scratch
  directory, so a developer's save is never touched). Every other PlayMode fixture still builds its own world.
- **The queue's fallback heartbeat is untested.** The runner is event-driven, so the tests do not
  sit on real polling delays — but the 60-second `FallbackHeartbeatMs` safety net is by
  construction the one path no test exercises, and it is exactly what would mask a future
  aggregator that forgets to raise `AvailabilityChanged`.
- **The fakes are hand-written**, and `FakeWindowsManager` in particular encodes an ordering
  constraint that mirrors production, so it can drift from production silently. The PlayMode
  fixture is the guard against that; no mocking framework is installed (and one would not
  solve that particular problem).
- **Time is re-anchored, but nothing validates a claim.** `TimeResyncTicker` (App layer) re-syncs
  every five minutes and, faster, whenever `ServerSyncedTimeProvider.IsSuspectedStale` reports that
  the device's wall clock and the monotonic stopwatch have diverged — which is what a resume from
  background looks like, since a monotonic clock does not tick while the device sleeps. That covers
  drift and suspension. What it does not cover is the general problem: there is still no server
  validating a claim, so `ITimeProvider` closes the date-change exploit and nothing more. A player
  with a debugger still lies, and the only real defence is the server refusing the grant.
- **Zenject is still vendored source** under `Assets/Zenject` rather than a package; it has its own
  asmdefs now, but updating it is still a manual copy.
- **`com.unity.ai.assistant` is in the manifest on purpose**, not left over from the template. It is
  the bridge an external agent drives this editor through — the tests and Play Mode checks for this
  project were run that way. It is an editor-only tool with no runtime
  code behind it and nothing in `Assets/Scripts` references it; a build that shipped this project
  would drop it, and that is the only reason to.

None of these are bugs — they're the deliberate scope cut for a take-home assessment — but they
are exactly the things a "let's turn this into a real feature" pass should revisit first.

## 11. Where the documentation lives

Two sets of documents, and the split is by *shape of knowledge* rather than by topic. Both hold
the reasoning that code comments leave out (see §8), so read the relevant one before changing a
subsystem.

`Docs/knowledge-base/` — cross-cutting knowledge: engine and library behaviour, things that were
measured rather than assumed, and the decisions that hold regardless of which class you are in.
[Index](knowledge-base/README.md).

- [`unity-ui-canvas-and-input.md`](knowledge-base/unity-ui-canvas-and-input.md) — split
  canvases, `sortingOrder` vs hierarchy order, raycasters, `overrideSorting`, layer bands.
- [`unitask-and-cancellation.md`](knowledge-base/unitask-and-cancellation.md) — `Share()` vs
  `Preserve()`, which token belongs where, transactions, `async void`/`.Forget()`.
- [`addressables-and-content.md`](knowledge-base/addressables-and-content.md) — handle
  lifetime, dedup, cached failures, the one blocking load, no code-built fallback.
- [`zenject-composition.md`](knowledge-base/zenject-composition.md) — concrete-first binding,
  typed factories vs `DiContainer`, multi-binding, disposal, the assembly graph.
- [`time-and-cooldowns.md`](knowledge-base/time-and-cooldowns.md) — server-anchored time,
  monotonic clocks, suspension, what cooldown semantics actually mean.
- [`pooling-and-ownership.md`](knowledge-base/pooling-and-ownership.md) — view pooling traps,
  texture ownership, close-ordering constraints.
- [`resilience.md`](knowledge-base/resilience.md) — what degrades, what is contained, what is
  deliberately hard.
- [`testing-in-unity.md`](knowledge-base/testing-in-unity.md) — harness mechanics: async test
  bodies, NUnit deadlocks, fake/production drift.
- [`persistence.md`](knowledge-base/persistence.md) — the inventory file as a cache of the
  server's truth: JSON over PlayerPrefs, atomic writes, serialised saves, what is not persisted.

`Docs/feature-maps/` — per-module documentation: what the files in one folder set are, how a call
flows through them, and the decisions specific to that subsystem.

- [`app.md`](feature-maps/app.md) — bootstrap, app-level state machine, `AppInstaller`.
- [`window-core.md`](feature-maps/window-core.md) — the generic window engine (registry,
  resolver, factory/pooling, manager, handle/instance/view, backdrop, transitions).
- [`window-queue.md`](feature-maps/window-queue.md) — priority queue, cooldowns,
  interrupts, aggregators.
- [`game-services.md`](feature-maps/game-services.md) — domain models, feature managers,
  the faked RPC layer.
- [`windows.md`](feature-maps/windows.md) — the six concrete windows built on the engine.
- [`inventory.md`](feature-maps/inventory.md) — the slot inventory: wallet vs. items,
  stacking rules, the hash contract, connect-time sync, file storage, the grant service and the
  inventory window.
- [`tests.md`](feature-maps/tests.md) — the EditMode/PlayMode split, what each suite covers,
  and how to extend either.
- [`ui-atlas.md`](feature-maps/ui-atlas.md) — the Lagoon Gold UI kit: the sprite set and how
  it is generated, the atlas, 9-slice borders, fonts, the layout rules every window prefab follows,
  and the preloader.

`Docs/process.md` — how the project is built with an AI agent: the division of labour, the contract
the agent works under, the verification loop, and a table of what went wrong and what caught it. Read
it before running a review or starting a session that changes more than one module.

`Docs/mockups/` — the UI kit's mockups as PNGs, exported from the "PopupSystem UI Kit" design
canvas so the visual reference travels with the repository: the `G · Lagoon Gold` style sheet, the
Unity spec board, the seven screen boards, the preloader and the logo concepts. Look at them before
composing a new screen; `Docs/mockups/README.md` says what each one is and how to re-export.

There is also one repository-level skill, `.claude/skills/ui-kit/SKILL.md` — the same kit as a
checklist an agent can act from: palette, sprite table, prefab rules, the three `Tools/UI Kit` menu
items, the failures that are silent, and the render-and-measure step that has to close the loop.
`ui-atlas.md` is the reasoning, the skill is the procedure. When they disagree, the feature map is
the older of the two.
