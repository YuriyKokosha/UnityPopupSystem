# Unity UGUI: canvases, draw order and input

Everything here was measured in the editor on this project, not taken from a forum post. It is the
knowledge behind `UILayerSorter`, `UIRoot`, `WindowsManager`'s ordering calls and every window
prefab's root.

## The UI is deliberately not one canvas

Each `UIRoot` layer (`Windows`/`Popups`/`Notifications`/`System`) carries its own `Canvas` and
`GraphicRaycaster`, and so does every window prefab and the modal backdrop.

The reason is rebuild cost. A `Canvas` rebuilds as a unit, and `FadeScaleWindowTransition` changes
`CanvasGroup.alpha` **and** `localScale` on every frame of every open and close. With a single
canvas that meant re-batching every graphic on screen — the base window, the backdrop, the
preloader — for the length of every animation. Now a fading window rebuilds only itself.

Splitting the canvas gives away two things a single canvas provided for free. Both are handled
explicitly, and both are easy to break by "tidying up" a prefab.

### 1. A nested Canvas receives no input without its own GraphicRaycaster

Not less input — none. A raycaster only collects graphics registered to its own canvas. A window
prefab whose root has a `Canvas` but no `GraphicRaycaster` renders perfectly and ignores every
click. Miss the `Canvas` instead and the window draws *underneath* every window that has one.

Copy an existing window prefab rather than building a root by hand. The root needs:
`Canvas` + `GraphicRaycaster` + `CanvasGroup`. The window prefabs leave `overrideSorting` off;
`UILayerSorter.Apply` turns it on and sets `sortingOrder` once the window is active in its layer.

### 2. Between canvases, hierarchy order decides nothing — `sortingOrder` does

This applies to input as well as rendering: `EventSystem` sorts raycast hits by sorting order
first, and only falls back to per-canvas graphic depth, which is not comparable across canvases.
With equal sorting orders, a click on a window's own button can legitimately resolve to the
backdrop behind it.

A window whose canvas kept the default `sortingOrder` of 0 would render at 0 no matter where it
sits in the hierarchy.

`UILayerSorter` is the answer. Sibling index stays the single source of truth for stacking —
`WindowFactory` and `ModalBackdropPresenter` keep using `SetAsLastSibling` and none of the
stacking logic knows about sorting — and the sorter derives
`sortingOrder = layerBase + siblingIndex + 1` from it after every open and close.

- `overrideSorting` is what makes `sortingOrder` mean anything on a nested canvas. Without it the
  value is stored and ignored, and the canvas inherits its parent's place in the order.
- Each layer's own `Canvas` carries the band its children are numbered up from (1000/2000/3000/4000
  in `MainScene`), so the numbering is defined in the scene, where the layers are, rather than
  hardcoded in the sorter.
- A child with no `Canvas` is left alone: it renders as part of the layer's own canvas, i.e. below
  every child that has one.

## `Canvas.overrideSorting` is ignored while the GameObject is inactive

Measured, not assumed. The setter does not take, and on activation `sortingOrder` snaps back to the
value inherited from the parent canvas.

Views are created hidden (`WindowFactory` ends with `view.Hide()`), so the ordering applied during
`OpenAsync`'s first backdrop refresh never sticks to the window being opened. `WindowsManager`
therefore orders a second time, in this exact sequence:

```
Show()  ->  UILayerSorter.Apply(parent)  ->  PlayOpenAsync()
```

Ordering after the transition would let the window fade in at the wrong depth for the length of the
animation.

## Layer bands and the child ceiling

`sortingOrder = band + siblingIndex + 1` gives each layer 999 children before it collides with the
next band. Pooled views stay parented to their layer while inactive and still occupy a slot, so the
practical ceiling is "distinct windows ever opened in this layer", not "open at once". Nowhere near
it today. A new layer needs a band that does not overlap its neighbours.

## Raycast hygiene

Non-interactive graphics have `raycastTarget` off — every `TextMeshProUGUI`, the progress bar, the
offer banner. What deliberately keeps it on: each `Button`'s own `Image`, the modal backdrop, the
preloader's full-screen overlay, and each window's `Background`/`Panel`, which are what make a
window swallow clicks aimed past it.

## Input blocking during a transition

Handled by the transition itself, not by the backdrop: `FadeScaleWindowTransition.PlayCloseAsync`
turns `CanvasGroup.blocksRaycasts`/`interactable` off the instant closing starts, so a window
cannot be clicked through while it is visibly fading out (mashing "Buy" during the fade-out, for
example). The mirror of that is `PlayOpenAsync` turning both back on — load-bearing, because views
are pooled and a reused view was last seen mid-close with both off.

## Other Unity behaviours worth knowing here

- `Object.Destroy` is deferred to the end of the frame. A null check on a destroyed object is only
  meaningful a frame later — which is why the PlayMode teardown assertions wait one.
- `StandaloneInputModule` reads the legacy `UnityEngine.Input`, which throws on every frame when
  the project is configured for the Input System package (as this one is). That is why
  `TestUiHierarchy` builds no `EventSystem` at all and asserts the input-ordering side of the
  canvas split through `sortingOrder` instead of simulating clicks.
