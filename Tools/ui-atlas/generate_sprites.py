# -*- coding: utf-8 -*-
"""
Sprite generator for the PopupSystem UI kit, style "G - Lagoon Gold".

Every sprite is drawn procedurally at 4x supersampling and saved 1:1 against the
1080x1920 reference: a sprite's pixel size equals the element's size in the
mockup when Canvas Scaler is set to Scale With Screen Size / 1080x1920.

Run:  python3 generate_sprites.py [output_folder]
Requires: Pillow.

White masters are tinted through Image.color (or Button.colors): body = the tint
itself, bevel = tint x0.78, drop shadow = tint x0.50. That is why one button
texture covers default / pressed / disabled without extra files.
"""
import json
import math
import pathlib
import sys

from PIL import Image, ImageDraw

SS = 4  # supersampling factor

GOLD = (239, 194, 78)
GOLD_DARK = (201, 143, 18)
CREAM = (255, 246, 227)
CREAM2 = (247, 233, 204)
FRAME_OUT = (10, 68, 104)
BLUE = (15, 111, 168)
BLUE_MID = (46, 135, 186)
BLUE_LIGHT = (90, 170, 214)
INK_SOFT = (155, 130, 86)

WHITE = (255, 255, 255)
BEVEL = (198, 198, 198)   # = tint x0.78
SHADOW = (128, 128, 128)  # = tint x0.50

OUT = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else ".") / "Sprites"
OUT.mkdir(parents=True, exist_ok=True)

SPRITES = {}


def spec(name, size, border, mode, tint, note):
    SPRITES[name] = {"size": size, "border_LBRT": border, "spriteMode": mode, "tint": tint, "note": note}


def mask_rr(w, h, box, radius):
    m = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(m)
    x0, y0, x1, y1 = box
    r = min(radius, (x1 - x0) / 2, (y1 - y0) / 2)
    d.rounded_rectangle([x0 * SS, y0 * SS, x1 * SS - 1, y1 * SS - 1], radius=r * SS, fill=255)
    return m


def layer(w, h, color, mask):
    l = Image.new("RGBA", (w * SS, h * SS), color + (0,))
    l.paste(Image.new("RGBA", (w * SS, h * SS), color + (255,)), (0, 0), mask)
    return l


def new(w, h):
    img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def rr(d, box, radius, fill):
    x0, y0, x1, y1 = box
    r = min(radius, (x1 - x0) / 2, (y1 - y0) / 2)
    d.rounded_rectangle([x0 * SS, y0 * SS, x1 * SS - 1, y1 * SS - 1], radius=r * SS, fill=fill)


def save(img, name):
    img.resize((img.width // SS, img.height // SS), Image.LANCZOS).save(OUT / name)


def beveled(w, h, body_h, radius, bevel, name):
    """Body + bottom bevel + drop shadow, as a white master."""
    body = mask_rr(w, h, (0, 0, w, body_h), radius)
    shade = mask_rr(w, h, (0, h - body_h, w, h), radius)
    band = Image.composite(mask_rr(w, h, (0, body_h - bevel, w, body_h), 0),
                           Image.new("L", (w * SS, h * SS), 0), body)
    img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    img.alpha_composite(layer(w, h, SHADOW, shade))
    img.alpha_composite(layer(w, h, WHITE, body))
    img.alpha_composite(layer(w, h, BEVEL, band))
    save(img, name)


# -- 1. window panel: outer frame + gold ring + cream body, colours baked in ---
img, d = new(160, 160)
rr(d, (0, 0, 160, 160), 44, FRAME_OUT + (255,))
rr(d, (10, 10, 150, 150), 36, GOLD + (255,))
rr(d, (16, 16, 144, 144), 30, CREAM + (255,))
save(img, "panel_9s.png")
spec("panel_9s.png", "160x160", [56, 56, 56, 56], "Single", "white",
     "whole window body: frame #0A4468 10px, gold #EFC24E 6px, cream #FFF6E3")

# -- 2-3. white masters for cards and pills -----------------------------------
img, d = new(96, 96)
rr(d, (0, 0, 96, 96), 26, WHITE + (255,))
save(img, "card_9s.png")
spec("card_9s.png", "96x96", [34, 34, 34, 34], "Single", "#F7E9CC",
     "cards inside the panel; same sprite tinted #0B4F7C when used on the stage")

img, d = new(96, 96)
rr(d, (0, 0, 96, 96), 48, WHITE + (255,))
save(img, "chip_9s.png")
spec("chip_9s.png", "96x96", [44, 44, 44, 44], "Single", "#0B4F7C",
     "balance badges, timer chip, the gold rule under a title")

# -- 4-6. buttons -------------------------------------------------------------
beveled(220, 140, 132, 66, 18, "btn_pill.png")
spec("btn_pill.png", "220x140", [74, 0, 74, 0], "Single",
     "#E29A1F / pressed #CC8813 / disabled #5E5A50",
     "fixed height 140 (132 body + 8 shadow), stretches horizontally only")

beveled(96, 104, 96, 48, 14, "btn_round_body.png")
spec("btn_round_body.png", "96x104", [0, 0, 0, 0], "Single", "#0A4468",
     "close and settings; the ring is a separate Image on top")

img, d = new(96, 96)
rr(d, (0, 0, 96, 96), 48, WHITE + (255,))
rr(d, (6, 6, 90, 90), 42, (0, 0, 0, 0))
save(img, "btn_round_ring.png")
spec("btn_round_ring.png", "96x96", [0, 0, 0, 0], "Single", "#FFEDC4",
     "6px ring around a round button")


# -- 7-10. icons as white masters ---------------------------------------------
def stroke(d, p0, p1, width):
    d.line([p0[0] * SS, p0[1] * SS, p1[0] * SS, p1[1] * SS], fill=WHITE + (255,),
           width=int(width * SS))
    r = width * SS / 2
    for p in (p0, p1):
        d.ellipse([p[0] * SS - r, p[1] * SS - r, p[0] * SS + r, p[1] * SS + r], fill=WHITE + (255,))


img, d = new(64, 64)
stroke(d, (18, 18), (46, 46), 7)
stroke(d, (46, 18), (18, 46), 7)
save(img, "icon_close.png")
spec("icon_close.png", "64x64", [0, 0, 0, 0], "Single", "#FFFFFF", "close cross")

img, d = new(64, 64)
for i in range(8):
    a = math.radians(i * 45)
    x, y = 32 + 23 * math.cos(a), 32 + 23 * math.sin(a)
    rr(d, (x - 6, y - 6, x + 6, y + 6), 3, WHITE + (255,))
d.ellipse([13 * SS, 13 * SS, 51 * SS, 51 * SS], fill=WHITE + (255,))
d.ellipse([24 * SS, 24 * SS, 40 * SS, 40 * SS], fill=(0, 0, 0, 0))
save(img, "icon_settings.png")
spec("icon_settings.png", "64x64", [0, 0, 0, 0], "Single", "#FFEDC4", "settings gear")

img, d = new(128, 128)
rr(d, (8, 26, 120, 56), 12, WHITE + (255,))
rr(d, (16, 62, 112, 110), 10, WHITE + (255,))
rr(d, (52, 50, 76, 80), 6, (0, 0, 0, 0))
rr(d, (56, 54, 72, 76), 5, WHITE + (255,))
save(img, "icon_chest.png")
spec("icon_chest.png", "128x128", [0, 0, 0, 0], "Single", "#FFE39B", "reward chest")

img, d = new(96, 96)
d.arc([9 * SS, 9 * SS, 87 * SS, 87 * SS], start=-90, end=180, fill=WHITE + (255,), width=10 * SS)
save(img, "spinner_ring.png")
spec("spinner_ring.png", "96x96", [0, 0, 0, 0], "Single", "#E29A1F",
     "270 degree arc, meant to be spun by rotating the RectTransform, not by an animated sprite")

# -- 11-12. coloured currency icons -------------------------------------------
img, d = new(96, 96)
d.ellipse([2 * SS, 2 * SS, 94 * SS, 94 * SS], fill=GOLD_DARK + (255,))
d.ellipse([7 * SS, 7 * SS, 89 * SS, 89 * SS], fill=GOLD + (255,))
d.ellipse([22 * SS, 22 * SS, 74 * SS, 74 * SS], outline=GOLD_DARK + (255,), width=5 * SS)
save(img, "icon_coin.png")
spec("icon_coin.png", "96x96", [0, 0, 0, 0], "Single", "white", "coin, colours baked in")

img, d = new(96, 96)
d.polygon([(28 * SS, 12 * SS), (68 * SS, 12 * SS), (84 * SS, 40 * SS),
           (48 * SS, 88 * SS), (12 * SS, 40 * SS)], fill=BLUE + (255,))
d.polygon([(28 * SS, 12 * SS), (68 * SS, 12 * SS), (60 * SS, 40 * SS),
           (36 * SS, 40 * SS)], fill=BLUE_LIGHT + (255,))
d.polygon([(68 * SS, 12 * SS), (84 * SS, 40 * SS), (60 * SS, 40 * SS)], fill=BLUE_MID + (255,))
save(img, "icon_gem.png")
spec("icon_gem.png", "96x96", [0, 0, 0, 0], "Single", "white", "gem, colours baked in")

# -- 13. banner placeholder ----------------------------------------------------
img, d = new(512, 256)
rr(d, (0, 0, 512, 256), 26, CREAM2 + (255,))
d.rounded_rectangle([156 * SS, 78 * SS, 356 * SS - 1, 178 * SS - 1], radius=12 * SS,
                    outline=INK_SOFT + (255,), width=5 * SS)
d.polygon([(168 * SS, 170 * SS), (214 * SS, 118 * SS), (252 * SS, 152 * SS),
           (282 * SS, 130 * SS), (344 * SS, 170 * SS)], fill=INK_SOFT + (255,))
d.ellipse([196 * SS, 94 * SS, 218 * SS, 116 * SS], fill=INK_SOFT + (255,))
save(img, "banner_fallback.png")
spec("banner_fallback.png", "512x256", [0, 0, 0, 0], "Single", "white",
     "shown when the remote offer banner fails to load")

manifest = {
    "style": "G - Lagoon Gold",
    "reference": "1080x1920, Pixels Per Unit 100, sprite sizes are 1:1 with the mockup",
    "borderOrder": "left, bottom, right, top - Unity order (Vector4 spriteBorder)",
    "tintMath": {"body": "tint", "bevel": "tint x0.78", "shadow": "tint x0.50"},
    "fonts": {"display": "Lilita One", "body": "Nunito"},
    "palette": {
        "stage": "#0E5C8C", "stageCard": "#0B4F7C", "frameOuter": "#0A4468",
        "gold": "#EFC24E", "goldLight": "#FFE39B", "ring": "#FFEDC4",
        "cream": "#FFF6E3", "creamCard": "#F7E9CC",
        "cta": "#E29A1F", "ctaPressed": "#CC8813", "ctaInk": "#3A2A16",
        "blue": "#0F6FA8", "ink": "#3A2A16", "inkMuted": "#7A6642",
        "disabledFill": "#5E5A50", "disabledInk": "#E7E1D3",
        "backdrop": "#052133 @ 72%",
    },
    "sprites": SPRITES,
}
(OUT.parent / "atlas-manifest.json").write_text(
    json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

for n, s in SPRITES.items():
    print("%-22s %-9s border=%s" % (n, s["size"], s["border_LBRT"]))
