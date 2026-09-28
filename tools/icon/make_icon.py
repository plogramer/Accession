"""Builds the application icon (src/Accession.App/Assets/Accession.ico and .svg) from the brand mark:
a white cube (a box of evidence) on the indigo-to-violet rounded square used in the app's sidebar.

Small sizes are drawn separately with thicker lines and no shading, so they stay crisp at 16-24 px.
Needs Python Playwright with a Chromium (to render the SVG) and Pillow:

    python3 tools/icon/make_icon.py
"""

import io
import os
from pathlib import Path

from PIL import Image
from playwright.sync_api import sync_playwright

REPO = Path(__file__).resolve().parents[2]
ASSETS = REPO / "src" / "Accession.App" / "Assets"
CHROMIUM = os.environ.get("CHROMIUM", "/opt/pw-browsers/chromium-1194/chrome-linux/chrome")
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def svg(size: int) -> str:
    small = size <= 24
    medium = size <= 48
    # The cube of the sidebar logo (24-unit box), scaled into a 256 canvas.
    stroke = 30 if small else 22 if medium else 17
    radius = 60 if small else 56
    scale = 8.6 if small else 8.0
    offset = 128 - 12 * scale
    shade = "" if small else f"""
      <path d="M4 7.5 12 3l8 4.5L12 12Z" fill="#fff" fill-opacity=".30"/>
      <path d="M4 7.5 12 12v9l-8-4.5Z" fill="#fff" fill-opacity=".10"/>"""
    glow = "" if medium else f"""
    <rect x="0" y="0" width="256" height="256" rx="{radius}" fill="url(#shine)"/>"""
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 256 256">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#6366f1"/>
      <stop offset="1" stop-color="#8b5cf6"/>
    </linearGradient>
    <linearGradient id="shine" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#fff" stop-opacity=".20"/>
      <stop offset=".55" stop-color="#fff" stop-opacity="0"/>
    </linearGradient>
  </defs>
  <rect x="0" y="0" width="256" height="256" rx="{radius}" fill="url(#bg)"/>{glow}
  <g transform="translate({offset} {offset}) scale({scale})">{shade}
    <g fill="none" stroke="#fff" stroke-width="{stroke / scale:.3f}" stroke-linejoin="round" stroke-linecap="round">
      <path d="M4 7.5 12 3l8 4.5v9L12 21l-8-4.5Z"/>
      <path d="M4 7.5 12 12l8-4.5M12 12v9"/>
    </g>
  </g>
</svg>"""


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    (ASSETS / "Accession.svg").write_text(svg(256), encoding="utf-8")
    images = {}
    with sync_playwright() as p:
        browser = p.chromium.launch(executable_path=CHROMIUM)
        for size in SIZES:
            page = browser.new_page(viewport={"width": size, "height": size}, device_scale_factor=1)
            page.set_content(f"<html><body style='margin:0;background:transparent'>{svg(size)}</body></html>")
            png = page.screenshot(omit_background=True, clip={"x": 0, "y": 0, "width": size, "height": size})
            images[size] = Image.open(io.BytesIO(png)).convert("RGBA")
            page.close()
        browser.close()

    images[256].save(ASSETS / "Accession.png")
    largest = images[256]
    largest.save(ASSETS / "Accession.ico", format="ICO", sizes=[(s, s) for s in SIZES],
                 append_images=[images[s] for s in SIZES if s != 256])
    print(f"Wrote {ASSETS / 'Accession.ico'} ({', '.join(str(s) for s in SIZES)} px)")


if __name__ == "__main__":
    main()
