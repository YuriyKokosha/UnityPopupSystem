---
name: ui-kit
description: Build or restyle a Unity UGUI window, popup, screen or overlay in this project so it matches the Lagoon Gold kit - sprites, palette, layout rules, atlas and font tooling, and the off-screen render check. Use whenever a prefab's visuals are being created or changed, a new window type is added, or someone asks why a screen looks off.
---

# UI kit (Lagoon Gold)

Every visual in this project comes from one kit. A new window does not get a new look; it is
assembled from the sprites and tokens that already exist.

- Reasoning and history: `Docs/feature-maps/ui-atlas.md`. Read it before changing the kit itself.
- Mockups: `Docs/mockups/` - `kit-lagoon-gold.png` (the style sheet), `unity-specs.png`, the seven
  `screen-*.png` boards and `preloader-as-built.png`. Look at them before composing a new screen.
  They are exported from the design canvas "PopupSystem UI Kit", which stays the source of truth;
  `Docs/mockups/README.md` says how to re-export.
- The workflow this plugs into: `Docs/architecture.md` section 6, "Adding a new popup/window type".

This file is the checklist. If it and the feature map disagree, the feature map is older - fix it.

## Palette

| Token | Hex | Where |
|---|---|---|
| stage | `#0E5C8C` | the screen behind everything |
| stage card | `#0B4F7C` | cards sitting on the stage, progress track |
| frame | `#0A4468` | the panel's outer frame, round button bodies |
| frame shadow | `#073553` | under the frame |
| gold | `#EFC24E` | the panel's gold band, rules, progress fill |
| gold light | `#FFE39B` | headings on dark, chest icon |
| ring | `#FFEDC4` | the ring around a round button, gear icon |
| cream | `#FFF6E3` | the panel's inner field |
| cream card | `#F7E9CC` | cards inside a panel |
| CTA | `#E29A1F` | primary button, normal |
| CTA pressed | `#CC8813` | primary button, pressed |
| CTA shadow | `#8F5D07` | baked into the sprite |
| dark on cream | `#8A5A08` | amber text on a cream field |
| ink | `#3A2A16` | body text, button labels |
| muted ink | `#7A6642` | secondary text on cream |
| disabled fill | `#5E5A50` | a disabled button |
| disabled ink | `#E7E1D3` | its label |
| backdrop | `#052133` @ 72 % | the modal dimmer |

Fonts: **Lilita One** for display (`Title`, button `Label`, `Amount`, `RewardLabel`), **Nunito** for
everything else. Text on a coloured fill must clear 4.5:1 - compute it, do not eyeball it.

## Sprites

All in `Assets/Content/UI/Sprites/`, packed into `Assets/Content/UI/UI.spriteatlasv2`.
Borders are Unity order: **left, bottom, right, top**.

| Sprite | Size | Border | Use |
|---|---|---|---|
| `panel_9s` | 160x160 | 56,56,56,56 | window body: frame + gold band + cream field |
| `card_9s` | 96x96 | 34,34,34,34 | cards, progress track and fill |
| `chip_9s` | 96x96 | 44,44,44,44 | badges, timer chip, the gold rule under a title |
| `btn_pill` | 220x140 | 74,0,74,0 | primary button, **height fixed at 140** |
| `btn_round_body` | 96x104 | - | round button body (also the `targetGraphic`) |
| `btn_round_ring` | 96x96 | - | its ring, as its own Image |
| `icon_close` `icon_settings` | 64x64 | - | cross, gear |
| `icon_chest` | 128x128 | - | reward |
| `spinner_ring` | 96x96 | - | 270 degree arc |
| `icon_coin` `icon_gem` `icon_energy` | 96x96 | - | currencies, colours baked in, addressable `UI/Currencies/<id>` |
| `item_sword` `item_potion` `item_arrows` `item_chest` `item_ore` | 128x128 | - | items, colours baked in, addressable at the catalog's `IconAddress` |
| `banner_fallback` | 512x256 | - | remote banner placeholder |
| `logo_lockup` | 346x321 | - | preloader logo |

Most are **white masters tinted at runtime**: body = 100 % of the tint, bevel = 78 %, shadow = 50 %.
A state change is a colour, not a new PNG.

A new sprite means editing `Tools/ui-atlas/generate_sprites.py` (Pillow, 4x supersampled) and
re-running `python3 Tools/ui-atlas/generate_sprites.py Assets/Content/UI`. Never hand-paint a PNG
into the folder - a palette change has to stay one edit plus a re-run. The logo is the one
exception, because it is type: vector in `Tools/ui-atlas/logo/logo_lockup.html`, rasterised by
`render_logo.py` next to it.

## Building a window prefab

Reference 1080x1920, `Scale With Screen Size`, match 0.5, PPU 100. Sprites are 1:1 with the mockup.

- Panel is `panel_9s`, `Image.type = Sliced`, centred, with an **explicit size**. Existing sizes:
  MainGame 920x560, Settings 860x360, DailyReward 860x680, Offer 860x900, RewardPopup 700x460,
  Inventory 860x940.
- **No `LayoutGroup` anywhere.** Blocks run top-down from a 34 px inset: title, gold rule, cards,
  CTA. The view pool reuses instances, and explicit positions mean nothing rebuilds on open.
- Buttons: `Image.color = white`, state carried by `Button.colors`. That is what makes `disabled` a
  real grey `#5E5A50` instead of a darkened amber.
- A disabled button's **label** needs `ButtonLabelTint` (`UI/Runtime/Widgets/`) with `#3A2A16` /
  `#E7E1D3`. Unity's ColorTint only recolours `targetGraphic`, so the dark label would otherwise sit
  at 1.7:1 on the grey fill.
- A round button is three objects: body (`btn_round_body`), ring (`btn_round_ring`), icon. The tint
  touches only the body.
- A **small action inside a card** (the inventory cell's `Use`/`Drop`, 72x40) is a `chip_9s` button
  with the same `Button.colors` + `ButtonLabelTint` treatment. `btn_pill` cannot shrink below 140.
- **Generated prefabs.** `InventoryWindow.prefab` and the inventory button on `MainGameWindow.prefab`
  come from `Tools/UI Kit/Inventory/*` (`UiKitInventoryWindowBuilder.cs`), not from hand edits:
  re-run the menu items after a kit change. That is the pattern for the next window too — a
  builder is a set of numbers that can be reviewed, a hand-built prefab is not.
- **Currencies and items are icons, never text.** Show them with an `IconAmountStripView` row fed by
  `RewardIcons` (preload in `OnInitializeAsync`, describe synchronously after). Rows are built by
  `Tools/UI Kit/Icons/Add icon rows to windows` (`UiKitRewardIconsBuilder.cs`); a new icon sprite
  also needs its entry in `IconAddresses` and `Tools/UI Kit/Icons/Mark icon sprites addressable`.
  Details and the per-window numbers: `ui-atlas.md`, "Reward icons".
- Close button, measured rather than derived: anchor `(1, 1)`, pivot `(0.5, 0.5)`, size
  `88 x 95.33`, `anchoredPosition (-34, -29.67)`, ring 88, icon 36. The extra 7.33 of height is
  `btn_round_body`'s drop shadow, which puts the circle's centre 3.67 above the rect's centre.
- A modal window's own `Background` is **transparent with `raycastTarget` off**. The dimmer is a
  separate object owned by `ModalBackdropPresenter`, sitting in the layer *below* the window; an
  opaque local backdrop covers it and `CloseOnBackdropClick` silently never fires.
- Where two controls mean the same thing at different times - a progress bar and a Retry button -
  give them **one slot at one position** and toggle, sized to the taller one. Stacked, the column
  changes height with the state and the whole screen reads as shifted.

## After touching sprites or fonts

Editor tooling lives in `Assets/Editor/UiKit/` (assembly `PopupSystem.EditorTools`, editor-only,
references `Unity.TextMeshPro`). It is in a subfolder on purpose: an asmdef directly in
`Assets/Editor/` would swallow the scripts already there.

- `Tools/UI Kit/Configure sprites and build atlas`
- `Tools/UI Kit/Build fonts and apply` - a new window whose text should be display needs its object
  name added to `DisplayObjects`.
- `Tools/UI Kit/Render prefab` - the render check below.
- `Tools/UI Kit/Icons/Mark icon sprites addressable`, `.../Add icon rows to windows`,
  `.../Render icon screens` - the reward icons: addresses, the rows in four windows, their renders.
- `Tools/UI Kit/Inventory/Build InventoryWindow prefab`, `.../Add inventory button to MainGameWindow`,
  `.../Render inventory screens` - the generated inventory UI and its three check renders.

## Traps that fail silently

- **Reimport renames sprite sub-assets.** `Configure sprites` forces `spriteMode = Single`, so
  `foo_0` becomes `foo`. Any `Image` still on the old name renders as Unity's white quad, with no
  error in the console. Re-assign and re-render.
- **`Sprite.rect` is not the source size.** Once packed, it reports the trimmed slot inside the
  atlas page; `Sprite.bounds` is the same number in units. Size a rect from the *texture*:
  `AssetDatabase.LoadAssetAtPath<Texture2D>(path).width/height`.
- **Atlas V2 has its own extension.** This project runs `Sprite Packer Mode = Sprite Atlas V2`; a
  `.spriteatlas` file in that mode is imported by `NativeFormatImporter` and packs nothing. Packing
  settings go through `SpriteAtlasImporter` - the identically named `SpriteAtlasAsset` methods are
  obsolete and do nothing.
- **`btn_pill` is sliced horizontally only** (top and bottom borders are zero on purpose - set them
  and Unity stretches the bevel band with the body). A button of a different height is a second
  sprite, not a different `sizeDelta`.
- **CSS numbers do not transfer.** A `button` defaults to `box-sizing: border-box`, and an
  absolutely positioned child is offset from its containing block's *padding* box. Measure in a
  browser instead of reading the mockup's CSS by hand.

## Verify by looking, not by reasoning

Every alignment claim in this kit that was reasoned about turned out wrong at least once. Before
calling a prefab done, render it off-screen and open the PNG.

**Use `UiKitPrefabRenderer`; do not build a render harness by hand.** Select the prefab in the
Project window and run `Tools/UI Kit/Render prefab`, or call the API from a command script:

```csharp
using PopupSystem.EditorTools;

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/Windows/OfferWindow.prefab");
UiKitPrefabRenderer.Render(prefab, "Claude outputs/UIKit/Renders/offer_loading.png", root =>
{
    // Drive the states through the view's own public methods, never by arranging objects by hand -
    // that renders the path the game actually takes.
    root.GetComponent<OfferWindowView>().SetContent(...);
});
```

It renders 1080x1920 (`ScreenSpaceCamera`, match 0.5) onto a `RenderTexture`, writes the PNG to
`Claude outputs/UIKit/Renders/` by default - outside `Assets/`, so it does not become an asset, and
inside the project's one gitignored scratch folder - and forces TMP text so it is not blank in the
first frame.

The reason it is a tool and not a snippet: it builds everything in a throwaway additive scene and
closes it in a `finally`. A hand-built harness builds in `MainScene`, and any exception before the
cleanup line leaves a camera, a canvas and a window instance in the scene you have open, which
then has to be reopened without saving. Deleting files through the Unity bridge is blocked too, so
scratch written into the repo lingers.

For centring, or "does it move between states", measure rather than squint: threshold the PNG
against the flat stage colour and compare the ink bounding boxes.

If you do need `PrefabUtility.LoadPrefabContents` for an edit, log **before**
`UnloadPrefabContents` - touching a transform afterwards throws `MissingReferenceException`, and by
then the prefab is already saved with whatever you set.

Comparing two full-screen crops is misleading when the panels are different heights; crop relative
to the panel corner.
