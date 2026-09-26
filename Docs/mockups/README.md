# Mockups

The UI kit's mockups, exported as PNGs so the reference travels with the repository. They are what
a new window is composed against; the numbers to build it with are in
[`../feature-maps/ui-atlas.md`](../feature-maps/ui-atlas.md) and in the `ui-kit` skill.

Source of truth is still the design canvas **"PopupSystem UI Kit"**
(<https://claude.ai/artifact/StriEqaVVayG583i7RbrHd>) — these files are an export of it, so a board
that changes there has to be exported again (see below). Everything here is 1:1 with the canvas
frames; the screens are 1080x1920, the same reference resolution the prefabs use.

| File | Board | Size | What it is |
|---|---|---|---|
| `kit-lagoon-gold.png` | G · Lagoon Gold | 1080x4500 | the chosen style sheet: palette, type scale, radii, buttons in every state, a window, badges, the loading states |
| `unity-specs.png` | Unity specs | 1240x3400 | the same kit as Unity instructions: canvas/layer setup, sorting bands, 9-slice borders, the asset table, which script draws what |
| `screen-main-game.png` | MainGame | 1080x1920 | base screen, layer 1000; everything opens on top of it |
| `screen-settings.png` | Settings | 1080x1920 | modal over MainGame, one dimmer |
| `screen-daily-reward.png` | DailyReward | 1080x1920 | chest, copy, CLAIM |
| `screen-offer-loaded.png` | Offer · content loaded | 1080x1920 | remote banner, price row, CTA |
| `screen-offer-loading.png` | Offer · loading | 1080x1920 | the same window while the remote content is still in flight |
| `screen-reward-pending.png` | RewardPopup · pending | 1080x1920 | RewardPopup over DailyReward: two modal layers, one backdrop |
| `screen-reward-result.png` | RewardPopup · result | 1080x1920 | the granted reward |
| `preloader-as-built.png` | Preloader · as built | 1440x1560 | the actual prefab rendered out of the editor, both states |
| `logo-concepts.png` | Preloader logo concepts | 1440x1780 | concept A is the one that shipped |

Two notes that live on the canvas and are worth carrying here:

- The **Offer · loading** board does not match the build: it shows the Buy button empty, amber and
  clickable while the content loads. The build disables it and shows "Loading...", which is the
  intended behaviour. Re-export the board the next time the canvas is touched; until then, the code
  (`OfferWindowController`) is the reference for that state.
- The rejected palette directions (Candy Chest, Royal Velvet, Sunny Pop, Deep Sea, Lagoon Light,
  Deep Marine, Soft Harbor) are not exported — they are exploration, and the kit is the Lagoon Gold
  sheet above. They are still on the canvas if a direction ever has to be revisited.

## Re-exporting

Each board is a self-contained `.dc.html` under `project/` in the canvas, with everything inline.
To refresh a PNG: read that file from the canvas, lift the `<helmet>` contents into `<head>` and the
`<x-dc>` contents into `<body>` (dropping the `support.js` line and the trailing `x-dc` script), swap
the Google Fonts link for `@font-face` rules pointing at the repository's own
`Assets/Content/UI/Fonts/**` TTFs, then screenshot it headless at the board's frame size.
