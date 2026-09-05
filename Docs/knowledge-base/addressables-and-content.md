# Addressables and content loading

Every UI prefab lives under `Assets/Content/UI/**`, is marked Addressable in the `UI` group, and is
loaded by **address** through `IUiPrefabProvider`. There is no `Resources` folder in the project.

## Why the port is async

The engine used to call `Resources.Load` directly. That meant two things: everything under
`Assets/Resources` shipped in the binary and was loaded into memory at startup whether or not it
was ever shown, and the load was *synchronous* — so `IWindowFactory` could be synchronous too, and
every caller above it was written on that assumption.

Swapping the loader for anything that goes to disk or the network would then have meant changing the
contract of the whole open path, up through `WindowsManager` and out to the queue. That is exactly
the kind of change that stops happening once a project is large. So the contract is async up front,
and what sits behind it — Addressables today, a CDN tomorrow — is one implementation away.

## Handle lifetime: kept until `Dispose`

`AddressablesUiPrefabProvider` holds every `AsyncOperationHandle<GameObject>` it loads for the life
of the session and releases them together on `Dispose` (bound as `IDisposable` in `AppInstaller`).

These prefabs are opened and closed over and over. Releasing one on close would mean re-loading it
on the next open, which is the opposite of what the view pool exists for. After the first open, the
"load" is a dictionary lookup.

That is right for seven windows and wrong for a real catalog. Anything that grows this beyond a
fixed, small set of always-needed prefabs needs a release policy first — see `CLAUDE.md` §10.

## Loads are deduplicated by address

Two windows opening in the same frame share one handle rather than racing two loads of the same
asset.

## A failed handle must not stay cached

A finished handle that did not succeed is not a cached answer, it is a cached error. Keeping it
meant one bad load — a connection dropped mid-download, a bundle not there yet — turned that
address into a permanent failure for the rest of the session: every later open of that window threw
instantly, without attempting anything, and indistinguishable from a genuinely missing asset.

The provider drops such an entry so the next caller starts a real load. A genuinely misconfigured
address still fails every time — just loudly on each attempt, rather than once for real and then
from cache.

## The three entry points, and the one blocking load

| Member | When to use it |
|---|---|
| `LoadAsync` | Everything. The default. |
| `GetLoaded` | Call sites that structurally cannot await — `ModalBackdropPresenter` runs inside `WindowsManager`'s synchronous open/close bookkeeping. Requires the address to have been preloaded. |
| `LoadBlocking` | Exactly one caller: whatever is shown *while* everything else loads. |

`LoadBlocking` is the project's one `WaitForCompletion` call, used by `AppInstaller` for the
preloader overlay. It is defensible there and nowhere else: there is nothing to put on screen during
the load of the loading screen itself, so the alternative is a black frame, not a smoother one. It
is a single small prefab, resolved once at startup.

The modal backdrop takes the other route. `AppEntryPoint.PreloadAlwaysNeededPrefabsAsync` loads it
before the first app state runs, so it is simply there by the time any modal window can open —
without a blocking load, and without making backdrop compositing asynchronous for the sake of one
asset. **Anything else that turns out to need the same treatment belongs in that preload list, not
in a blocking load at its own call site.**

## There is no code-built fallback window

Every `WindowDefinition` must carry an address that resolves in the Addressables catalog, or
`WindowFactory.CreateAsync` throws `InvalidOperationException`. A caller that got `null` back would
fail later and further from the cause.

This is intentional — prefab-only content, nothing built from code — but it means a prefab that was
moved out of `Assets/Content`, or lost its Addressable flag, is a hard failure rather than a
degraded placeholder. **A prefab that exists on disk but is not marked Addressable fails exactly
the same way as one that was deleted: check the group before checking the file.**

`WindowFactory` also validates the instantiated view against `WindowDefinition.ViewType` before
anything is put on screen. Without that check, a wrong address next to a right `ViewType` surfaces
much later — the window opens, the view is parented and shown, and
`WindowController<TData, TView>` throws "Expected view of type X, got Y" from inside the
controller, naming neither the window type nor the address that produced the wrong prefab. If the
instance is rejected it is destroyed before the throw; leaving it parented under the layer would
put an unreachable window on screen for the rest of the session.

## StreamingAssets is not a path on every platform

Relevant to `RemoteImageLoader`'s base URL, wired in `AppInstaller`. On Android
`Application.streamingAssetsPath` is *already* a URL of the form `jar:file:///…/base.apk!/assets`,
so unconditionally prefixing `file://` produced an unresolvable address there and every banner load
failed straight into the offer's fallback copy — invisibly in the editor and on iOS, where the path
really is a bare path. Only add the scheme when there is not one.

## Editor behaviour

Under the editor's asset-database Addressables mode, `LoadBlocking` returns immediately — which is
why the PlayMode fixtures can reproduce the app's preload precondition in `SetUp` without cost.
Because the PlayMode suite uses the real provider, it is also the only automated check that the
addresses configured in the `WindowDefinition`s actually resolve.
