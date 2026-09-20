# UI atlas (style G - Lagoon Gold)

The sprite set all five windows are built from, and the rules behind it.
The mockups are exported to [`../mockups/`](../mockups/) - the `G - Lagoon Gold` sheet, the Unity
spec board and the seven screen boards - so the reference travels with the repository. They come
from the Design canvas "PopupSystem UI Kit", which stays the source of truth (and still holds the
rejected palette directions); `../mockups/README.md` says how to re-export a board.

## Where things live

| Path | What it is |
|---|---|
| `Docs/mockups/` | the canvas boards as PNGs: the style sheet, the Unity specs, the seven screens |
| `Assets/Content/UI/Sprites/` | 14 PNGs; no Addressables group touches them - the atlas pulls them in |
| `Assets/Content/UI/UI.spriteatlasv2` | the atlas, packed from that folder as a whole |
| `Assets/Content/UI/Fonts/` | Lilita One and Nunito plus the TMP SDF assets built from them |
| `Assets/Editor/UiKit/` | editor tooling in its own assembly: sprite/atlas setup, font setup and the off-screen prefab renderer, all three under `Tools/UI Kit` |
| `Tools/ui-atlas/generate_sprites.py` | PNG generator (Pillow); the sprites are drawn in code |
| `Tools/ui-atlas/logo/` | the logo lockup: its vector source and the script that rasterises it |
| `Claude outputs/UIKit/Renders/` | scratch proofs written by `Tools/UI Kit/Render prefab`; nothing reads them, and the folder is gitignored |

The sprites are generated rather than hand-painted, so a palette change is a change
to a few constants plus a re-run, not a repaint of fourteen files.
Run: `python3 Tools/ui-atlas/generate_sprites.py Assets/Content/UI` - it writes the
PNGs into `Sprites/` next to it and refreshes `atlas-manifest.json`.

`logo_lockup.png` is the exception: it is type, so it cannot be drawn in Pillow. It is
SVG plus a Lilita One wordmark in `Tools/ui-atlas/logo/logo_lockup.html`, rasterised by
`Tools/ui-atlas/logo/render_logo.py` (headless Chromium at 3x, alpha-bounds crop,
Lanczos down to 1x). Edit the HTML, never the PNG.

## Sizes and scale

Reference is 1080x1920, `Canvas Scaler = Scale With Screen Size`, match 0.5,
`Pixels Per Unit = 100`. Sprites are saved 1:1 against the mockup: a button that is
132 px tall in the mockup is 132 px tall in the texture, with no intermediate scaling.

## Sprites

Borders are given in Unity order - **left, bottom, right, top**.

| Sprite | Size | Border | Tint | Purpose |
|---|---|---|---|---|
| `panel_9s` | 160x160 | 56, 56, 56, 56 | white | the whole window body: frame `#0A4468` 10 px, gold `#EFC24E` 6 px, cream `#FFF6E3` |
| `card_9s` | 96x96 | 34, 34, 34, 34 | `#F7E9CC` | cards inside the panel; same sprite tinted `#0B4F7C` on the stage |
| `chip_9s` | 96x96 | 44, 44, 44, 44 | `#0B4F7C` | balance badges, timer chip, the gold rule under a title |
| `btn_pill` | 220x140 | 74, 0, 74, 0 | `#E29A1F` / `#CC8813` / `#5E5A50` | the primary button, all three states |
| `btn_round_body` | 96x104 | - | `#0A4468` | round close and settings buttons |
| `btn_round_ring` | 96x96 | - | `#FFEDC4` | the ring around a round button, as its own Image |
| `icon_close` | 64x64 | - | `#FFFFFF` | close cross |
| `icon_settings` | 64x64 | - | `#FFEDC4` | settings gear |
| `icon_chest` | 128x128 | - | `#FFE39B` | reward |
| `spinner_ring` | 96x96 | - | `#E29A1F` | 270 degree arc |
| `icon_coin` | 96x96 | - | white | coin, colours baked in |
| `icon_gem` | 96x96 | - | white | gem, colours baked in |
| `banner_fallback` | 512x256 | - | white | placeholder for the remote offer banner |
| `logo_lockup` | 346x321 | - | white | the preloader logo; colours baked in, not drawn by the generator |

## Three decisions worth knowing before editing

**White masters instead of coloured copies.** Everything except the panel, the coin,
the gem and the banner placeholder is drawn white and tinted at runtime. The bevel and
the drop shadow are baked as lightness rather than as separate colours: body = 100 % of
the tint, bevel = 78 %, shadow = 50 %. So `default`, `pressed` and `disabled` are one
texture and three colour values, and changing the accent needs no new PNG. The trade-off:
the shadow under a `disabled` button goes grey instead of disappearing as it does in the
mockup - if that matters, give it its own child Image with an alpha of its own.

**The button is sliced horizontally only.** `btn_pill` is 220x140, where 132 is the body
and 8 the drop shadow. The top and bottom borders are deliberately zero: set them and Unity
stretches the bevel band along with the body, and the volume falls apart. That means the
button height in a prefab is fixed at 140. A button of a different height is a second
sprite, not a different `sizeDelta`.

**Textures are uncompressed.** `TextureImporterCompression.Uncompressed`, `maxTextureSize 512`,
mips off. Across thirteen sprites this size that is cheaper than ASTC artefacts on flat fills
and on the gold frame. The atlas itself is packed as `CompressedHQ` - the assembled page is
compressed, not every source file.

## Atlas and Addressables

`UI.spriteatlasv2` is packed from the folder as a whole (`Tools/UI Kit/Configure sprites and
build atlas`) and is marked `IncludeInBuild = true`. Window prefabs stay Addressable and pull
the sprites in as a dependency of the atlas.

The extension matters. This project runs `Sprite Packer Mode = Sprite Atlas V2`
(`m_SpritePackerMode: 5`), and V2 has its own asset type and its own extension. A `.spriteatlas`
file in that mode is imported by the plain `NativeFormatImporter`: it looks like it was created,
but it packs nothing and no sprite ends up inside. So the script reads
`EditorSettings.spritePackerMode` and picks `.spriteatlasv2` or `.spriteatlas` itself; under V2
the packing settings go through `SpriteAtlasImporter`, not through the asset's own methods - the
identically named `SpriteAtlasAsset` methods are obsolete and silently do nothing.

`IncludeInBuild = true` is a deliberate prototype compromise: the atlas lands both in the main
build and in the Addressables dependencies, so the data is duplicated. Production would mark the
atlas itself Addressable and turn `IncludeInBuild` off, but then sprite loading becomes async and
has to be awaited before the first window is shown. With five windows and a single atlas page,
the duplication is cheaper than that asynchrony.

Verified on this project: the atlas is packed, it holds 13 sprites, and the 9-slice borders reach
`Sprite.border` (`panel_9s` -> 56/56/56/56, `btn_pill` -> 74/0/74/0).

## Rendering a prefab to look at it

`Tools/UI Kit/Render prefab` (`UiKitPrefabRenderer`) renders the selected prefab asset to a
1080x1920 PNG in `Claude outputs/UIKit/Renders/`. Every alignment claim in this kit that was reasoned
about turned out wrong at least once, so the render is how a layout claim gets closed.

It exists as a menu item rather than as a throwaway command script because of how it used to fail.
The harness was written fresh each time, it built its camera and canvas in whatever scene was open
- `MainScene` - and any exception before the cleanup line left the camera, the canvas and an
instantiated window sitting in that scene as unsaved changes. The only way back was to reopen the
scene without saving.

What the tool does about that, in order:

- **The work happens in a scratch scene.** `EditorSceneManager.NewScene(..., Additive)` first, then
  a single root `GameObject` that is moved into it with `SceneManager.MoveGameObjectToScene` before
  anything else can throw - new objects otherwise land in the *active* scene, which is the one the
  user has open.
- **The scene is closed in a `finally`**, along with the root, the `RenderTexture` and the readback
  `Texture2D`. A `configure` callback that throws (verified with a deliberate one) leaves the open
  scene byte-identical and not even dirty.
- **The camera culls everything but the UI layer and is `enabled = false`.** It renders on demand
  through `camera.Render()`, so it never contributes to the editor's own frames, and nothing from
  the scenes that stay loaded can drift into the frame. Screen-space *overlay* canvases are not
  rendered by cameras at all, which is why `MainScene`'s `UIRoot` cannot appear in the PNG.
- **The target texture is assigned before the canvas is built**, because a `ScreenSpaceCamera`
  canvas takes its size from the camera's pixel rect - which is the render texture's size once one
  is set, and the game view's otherwise.
- **TMP text is forced twice.** `Canvas.ForceUpdateCanvases()`, then `ForceMeshUpdate()` on every
  `TMP_Text`, then `ForceUpdateCanvases()` again: text laid out in the same frame it was
  instantiated in renders blank otherwise.
- **The PNG is written outside `Assets/`** so it does not become an imported asset, and into
  `Claude outputs/` so that the project needs exactly one gitignored scratch folder rather than one
  per tool. It matters that it is gitignored at all: scratch written into the repo through the
  editor bridge lingers, because deleting files there is blocked.

`Render(prefab, outputPath, configure, background, width, height)` is public. Drive a window's
states from `configure` through **the view's own public methods**, not by arranging objects by
hand - that renders the path the game actually takes. A dynamically compiled command script can
call it directly (`using PopupSystem.EditorTools;`), which is how a state-by-state proof gets made
without another hand-built harness.

For centring, or "does it move between states", threshold the PNG against the flat stage colour
and compare ink bounding boxes rather than squinting; crop relative to the panel corner, because
comparing two full-screen crops of differently sized panels is misleading.

## Window prefabs

The five prefabs in `Assets/Content/UI/Windows/` are rebuilt on these sprites. The hierarchy is
unchanged (`Root -> Background -> Panel -> ...`), so every `[SerializeField]` reference on the
views survived - checked, zero broken.

Layout rules:

- The panel is `panel_9s` with `Image.type = Sliced`, anchored to the centre of the screen, with an
  explicit size: MainGame 920x560, Settings 860x360, DailyReward 860x680, Offer 860x900,
  RewardPopup 700x460.
- Blocks inside a panel are placed top-down from a 34 px inset: title, gold rule, cards, CTA. No
  `LayoutGroup` anywhere - positions are explicit so the view pool does not rebuild the layout on
  every open.
- Buttons are tinted through `Button.colors`, not `Image.color`: `Image.color = white` and
  `normalColor` carries the state. That way `disabled` gets a real grey `#5E5A50` instead of a
  darkened amber, and `SetActionInteractable(false)` looks the way the mockup does.
- A round button is three objects: the body (`btn_round_body`, which is also the `targetGraphic`),
  the ring (`btn_round_ring`) and the icon. The button tint only touches the body.
- The close button hangs off the panel's top-right corner against the `(1, 1)` anchor: an 88 px
  circle whose centre sits 34 px inside the right edge and 26 px below the top one. Do not derive
  those numbers from the mockup's CSS by hand - they were measured in a browser, because two
  details bite. A `button` defaults to `box-sizing: border-box`, so the mockup's `width: 88px` plus
  a 5 px ring is an 88 px circle, not 98. And an absolutely positioned child is offset from its
  containing block's *padding* box, so the mockup's `top: -34px; right: -26px` is measured from
  inside the cream box's own 6 px border, on top of the frame's 10 px padding. The rect itself is
  88 x 95.33 because `btn_round_body` carries 8 px of drop shadow below the circle, which puts the
  circle's centre 3.67 px above the rect's centre - hence `anchoredPosition (-34, -29.67)`.

### Fixed along the way

The full-screen `Background` inside every modal window used to be the dimmer itself (black at 45 %)
with `raycastTarget = true`. But the dimmer is drawn by `ModalBackdropPresenter` as a separate
object that sits in the layer *below* the window - so the window's own backdrop covered it and a tap
outside the panel never reached it, which means `CloseOnBackdropClick` could not fire at all. Modal
windows now have a transparent `Background` that does not take raycasts; there is a single dimmer,
owned by the engine, recoloured to `#052133` at 72 %.

Two project-level changes were needed for the mockup to match the screen: the `CanvasScaler` on
`UIRoot` moved from 1920x1080 to **1080x1920**, and `PlayerSettings` moved to portrait.

### Fonts

The kit's faces are Lilita One (display) and Nunito (body), both under the SIL Open Font License.
`Assets/Content/UI/Fonts/` is flat and holds only what is used: the two upright Regular `.ttf`
files, the TMP SDF asset built from each, and both `OFL` licence texts. A Google Fonts archive
unpacks into subfolders with every weight, both italics and a variable font - 15 files, 2.6 MB,
none of them referenced by anything - so it gets flattened rather than committed whole.
`Tools/UI Kit/Build fonts and apply` builds a TMP SDF asset next to each `.ttf`
(`CreateFontAsset`, 90 pt sampling, 1024 atlas, dynamic population, atlas texture and material
stored inside the asset) and assigns them across all five prefabs by role: the display face on
`Title`, button `Label`, `Amount` and `RewardLabel`, the body face on everything else. A new window
whose text should be display needs its object name added to `DisplayObjects`.

The matcher keeps the two things a Google Fonts download brings along, even though the folder no
longer contains them: the italics and the variable font. `Nunito-Italic-VariableFont_wght.ttf`
sorts first alphabetically, so a naive "first file containing Nunito" picks an italic variable
face. The matcher drops anything with `Italic` in the name, prefers static over `Variable`, then
prefers `Regular` - which is what makes dropping a fresh archive in and re-running safe.

The editor tooling lives in `Assets/Editor/UiKit/` with its own assembly definition
(`PopupSystem.EditorTools`, editor-only, referencing `Unity.TextMeshPro`). It sits in a subfolder on
purpose: an asmdef directly in `Assets/Editor/` would swallow the scripts already there, such as
`PlayModeResultProbe.cs`, and cut them off from the references the predefined editor assembly gives
them for free.

### Disabled labels

`Button`'s ColorTint transition only recolours the Selectable's `targetGraphic`, so on a greyed-out
fill the kit's dark label (`#3A2A16` on `#5E5A50`) falls to about 1.7:1. `ButtonLabelTint`
(`UI/Runtime/Widgets/`) carries the two label colours as serialized fields and swaps them when
`interactable` flips, which brings the disabled state back to `#E7E1D3` at 5.4:1. It sits on all
three CTAs - `ClaimButton`, `BuyButton`, `OkButton` - because every one of them goes disabled at
some point in its flow.

The component polls in `LateUpdate` behind a changed-value guard. `Selectable` raises no event when
`interactable` flips, and the alternative - subclassing `Button` to override `DoStateTransition` -
would put a custom component on every button in the project rather than only on the ones with a
label to protect.

### RewardPopup's OK

`RewardPopupView` gained an `_okButton` next to the close cross; both call `RequestClose`.
`SetLoading` disables OK while the claim or purchase is still in flight, so the popup cannot be
dismissed from under a transaction that is halfway through. The close cross is deliberately left
enabled: the player must always be able to walk away, and the transaction itself runs under
`CancellationToken.None` in the controllers, so closing does not abandon it.

### Preloader

`PreloaderOverlay.prefab` follows the same tokens but is not a window: it covers an empty screen
during boot and during the connect retries, before `MainGame` exists. So its full-screen `Overlay`
is not a dimmer but the stage itself - solid `#0E5C8C` with `raycastTarget` on, which is also what
swallows input while the app is not ready.

Composition: one centred column - logo at `y +175`, message at `y -85`, and a **status slot** at
`y -265` that holds either the progress bar or the Retry button, never both. The first draft stacked
them (bar at `-90`, button at `-280`), which meant the column was 140 px shorter whenever there was
no error, and the whole screen read as shifted upward during a normal boot. One slot fixes that: the
logo and the message do not move between the two states, and the measured ink starts at the same
`y = 625` in both.

The slot is sized to the button (140), because `btn_pill` has a fixed height - see the slicing note
above. The bar is 72 and centres inside it. `PreloaderOverlayView.SetErrorVisible` is what keeps them
mutually exclusive; the view needs a reference to the bar's **root**, not just to `_progressFill`,
which is why `_progressBar` exists as its own serialized field.

The bar itself is `card_9s` twice - the track tinted `#0B4F7C`, the fill `#EFC24E` - rather than
`chip_9s`, because a pill's 44 px borders do not fit a 72 px tall rect and Unity would squash them.
`SetProgress` drives the fill through `anchorMax.x` and zeroes both offsets, so the fill keeps
`anchorMin (0, 0)` and grows from the left.

The logo is `logo_lockup` as a plain `Image` at its native 346 x 321, `preserveAspect` on, no field
on `PreloaderOverlayView` - it is a constant, not state. It is the mark the library describes:
three panels stacked on a layer, over `Popup` in cream and `System` in gold.

Two things bite when swapping that sprite, both of them silent:

- **`Sprite.rect` is not the source size.** Once a sprite is packed into an atlas it reports its
  place inside the page, already trimmed of transparent padding - here 263 x 209 instead of
  346 x 321. `Sprite.bounds` is the same number in units. Size the rect from the *texture*
  (`AssetDatabase.LoadAssetAtPath<Texture2D>(path).width/height`), or `preserveAspect` letterboxes
  the logo inside a rect of the wrong shape.
- **Reimporting the PNG drops the reference.** `Tools/UI Kit/Configure sprites` forces
  `spriteMode = Single`, which renames the sprite sub-asset (`logo_lockup_0` -> `logo_lockup`). Any
  `Image` pointing at the old name silently falls back to Unity's white quad - a white box on the
  boot screen, with no error. Re-assign `Image.sprite` after a reimport and re-render to check.

### Spinners

The three `spinner_ring` instances - `OfferWindow/.../BannerLoading/Ring`,
`RewardPopupWindow/.../LoadingState/Ring`, `SettingsWindow/.../Loading/Ring` - turn under an
`Animator` pointed at `Assets/Content/UI/Animation/SpinnerRing.controller`, one turn per second,
clockwise. No script: the clip and the controller are assets, so the speed is editable without a
recompile, and nothing new ends up on the windows' own components.

Four details in that clip are not optional:

- The curve animates **`localEulerAnglesRaw.z`**, not `localRotation`. A quaternion cannot hold a
  full turn - interpolating 0 to 360 takes the short way round and the ring sits still. A wrong
  binding path is also silent: the clip plays, nothing moves, no warning. Sample it
  (`clip.SampleAnimation`) and read the angle back rather than trusting it.
- It runs **0 to -360** over exactly 1 s with linear tangents. The last key equals the first modulo
  a turn, which is what makes the loop seamless; default smooth tangents would ease into the seam
  and the ring would visibly hesitate once a second.
- `updateMode = UnscaledTime`, so the spinner keeps moving if anything pauses the game clock -
  a frozen spinner reads as a hung app.
- `keepAnimatorStateOnDisable = false`, because views are pooled: otherwise the next window's ring
  resumes at whatever angle the previous one left behind.

The clip and controller are ordinary dependencies of the Addressable window prefabs, so they ride
into the `UI` group with them and need no entry of their own.

An `Animator` per spinner is heavier than a five-line `MonoBehaviour` doing
`transform.Rotate(0, 0, -360 * dt)`. Three of them, only alive while a loading state is on screen,
is not a budget worth defending - and this way the rotation is an asset a designer can retime.
