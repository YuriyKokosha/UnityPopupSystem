#!/usr/bin/env python3
"""Render logo_lockup.html into Assets/Content/UI/Sprites/logo_lockup.png.

Usage: python3 Tools/ui-atlas/logo/render_logo.py [<repo root>]

Renders at SCALE, crops to the real alpha bounds (a box crop shaves Lilita's
caps) and downscales to 1x with a Lanczos filter.
"""
import sys, pathlib
from PIL import Image
from playwright.sync_api import sync_playwright

SCALE = 3
HERE = pathlib.Path(__file__).resolve().parent
ROOT = pathlib.Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else HERE.parents[2]
SRC = HERE / "logo_lockup.html"
OUT = ROOT / "Assets" / "Content" / "UI" / "Sprites" / "logo_lockup.png"
RAW = HERE / "_raw.png"

with sync_playwright() as p:
    browser = p.chromium.launch()
    page = browser.new_page(viewport={"width": 900, "height": 700},
                            device_scale_factor=SCALE)
    page.goto(SRC.as_uri())
    page.wait_for_timeout(600)
    page.screenshot(path=str(RAW), omit_background=True)
    browser.close()

img = Image.open(RAW).convert("RGBA")
box = img.getchannel("A").getbbox()
if box is None:
    raise SystemExit("nothing rendered - the page came out empty")
img = img.crop(box)
w, h = img.size
img = img.resize((round(w / SCALE), round(h / SCALE)), Image.LANCZOS)
OUT.parent.mkdir(parents=True, exist_ok=True)
img.save(OUT)
RAW.unlink()
print(f"{OUT} -> {img.size[0]} x {img.size[1]}")
