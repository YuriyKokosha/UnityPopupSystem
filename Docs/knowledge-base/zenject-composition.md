# Zenject, composition and assemblies

`AppInstaller` is the single composition root. Everything is constructor-injected; there is no field
injection, no service locator, and no `FindObjectOfType` anywhere in the engine.

## Concrete type first, then the port, then `IDisposable`

The recurring binding shape:

```csharp
Container.Bind<AddressablesUiPrefabProvider>().AsSingle();
Container.Bind<IUiPrefabProvider>().To<AddressablesUiPrefabProvider>().FromResolve();
Container.Bind<IDisposable>().To<AddressablesUiPrefabProvider>().FromResolve();
```

`FromResolve` looks the **concrete** type up, so binding only the interface leaves it with nothing
to find. Binding the concrete type first is what makes the port and the `IDisposable` resolve to
*one* instance instead of two.

That matters for everything that owns something releasable, and every one of these owns something:

| Type | What it owns |
|---|---|
| `AddressablesUiPrefabProvider` | Every Addressables handle it took |
| `RemoteImageLoader` | Every texture it downloaded and cached |
| `WindowFactory` | Pooled view GameObjects |
| `WindowsManager` | Whatever windows are still on screen — their controllers' subscriptions and lifetime tokens |
| `DailyRewardWindowAggregator` | A subscription to `DailyRewardManager` |
| `WindowQueueRunner` | Subscriptions to every aggregator and to the windows manager |
| `AppStateManager` | The current state's teardown |

**The rule that follows: if a class subscribes to anything, bind it as `IDisposable`.** The
aggregator above was the one place in the project that broke its own rule, and it is the kind of
omission that gets copied into the next aggregator someone writes. Skipping the binding leaks the
subscription rather than failing loudly.

`ServerSyncedTimeProvider` is bound as the concrete type for a different reason:
`AppConnectServerState` needs `SyncAsync()`, which is deliberately not on `ITimeProvider`. Reading
the clock is everyone's business; setting it is the connect state's alone.

## Nothing outside `AppInstaller` injects `DiContainer`

A class that needs to create objects at runtime asks for the specific capability *by type*:

- each `IWindowModule` takes `IFactory<its own controller>`;
- `WindowFactory` takes `PrefabFactory<WindowView>` — "instantiate this prefab and inject its
  components" is the one capability it needs.

That distinction is the whole difference between a dependency and a service locator, and it is what
makes those classes constructible in a test without a container. The PlayMode fixtures make the
same typed-factory bindings the installer does, and resolve nothing arbitrary.

`ModalBackdropPresenter` is the deliberate exception in the other direction: it uses
`Object.Instantiate` rather than a factory, because the backdrop prefab is a full-screen `Image`
with a `Button` and nothing else — there is nothing on it to inject. It is constructed by
`WindowsManager` with `new` precisely because it needs nothing from the container. The moment a
backdrop grows a component with dependencies is the moment to add the factory, not before.

## Extend by multi-binding, never by a central `switch`

`IWindowModule` and `IWindowQueueAggregator` are both Zenject multi-bindings consumed generically.
`WindowRegistry` and `WindowControllerResolver` only ever iterate the bound modules, so adding a
window type never means editing either of them; `WindowQueueRunner` treats aggregators the same
way. This is the project's one recurring extensibility pattern and new features should follow it
rather than add a branch somewhere central. The full workflow is `CLAUDE.md` §6.

## `ITickable` is a container concept — so it stays in `App`

`TimeResyncTicker` lives in `App` rather than next to `ServerSyncedTimeProvider` for two reasons.
Deciding to go to the network is a composition-root decision: `Game` knows how to read a clock and
how to anchor one, not when the application should spend a request on it. And keeping the tickable
in `App` is what keeps Zenject out of `PopupSystem.Game` entirely.

## Shutdown actually has to run

`AppStateManager.ExitAsync` only ever ran for a state being *replaced* by the next one, so the last
state in the sequence — `AppMainGameState` — never exited at all. Its teardown was unreachable dead
code, which is why `WindowQueueRunner.StopIdleMonitoring` was never called and the idle monitor's
`CancellationTokenSource` was neither cancelled nor disposed: the loop simply outlived the state
that started it. `AppStateManager` is bound as `IDisposable` so the container runs teardown when the
scene or app goes away.

Teardown from `Dispose` has to be synchronous. Every `ExitAsync` in the sequence completes
synchronously today (they cancel tokens and stop loops rather than awaiting), so nothing is lost —
but a state that genuinely needs to await while shutting down will need a real async shutdown path
instead.

`WindowsManager.Dispose` is the same story for whatever is still on screen. It is deliberately *not*
a close: nothing awaits a transition, there is nobody left to watch a fade-out, and every handle
still being awaited by a controller flow or by the queue is completed rather than left parked
forever. Without it, shutting down left every open window's controller subscribed to what it had
subscribed to and its lifetime token neither cancelled nor disposed — invisible in a single-scene
demo, a leak per window per load the moment a scene is unloaded and reloaded.

## Assemblies enforce the layering

Each layer is its own assembly definition, so the dependency direction is a compile error rather
than a code-review finding. The table is in `CLAUDE.md` §3.

Internals are granted **by name, not by blanket**. `WindowHandle.SetState()/MarkClosed()` are
`internal` to `PopupSystem.Contracts` because advancing a window's lifecycle is the engine's job and
no caller holding a handle should be able to fake a state change.
`Assets/Scripts/Contracts/AssemblyInfo.cs` names the two assemblies that legitimately need it:

- `PopupSystem.UI` — `WindowsManager`, the one type that drives the handles it hands out;
- `PopupSystem.Tests.EditMode` — `FakeWindowsManager`, which has to mirror that driving in order to
  test `WindowQueueRunner` without a scene or a container.

This replaced `InternalsVisibleTo("Assembly-CSharp-Editor")`, which existed only because the project
had no assembly definitions at all: runtime code sat in the predefined `Assembly-CSharp`, a custom
test asmdef cannot reference that assembly, and the workaround was to fall back on the "magic Editor
folder" convention and open every internal in the project to the whole editor-default assembly.

One consequence to know before sharing test helpers: `PopupSystem.Tests.EditMode` is an
**Editor-only** assembly, and an assembly with no platform restrictions cannot reference one that
has them. That is why the PlayMode suite has its own small fake clock rather than reusing the
EditMode `FakeTimeProvider`. Sharing them would mean a third assembly for fakes — worth doing when
more than one is duplicated, not for one.
