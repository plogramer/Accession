"""Makes the screenshots used by the help pages (src/Accession.App/wwwroot/help/images).

The pictures come from the web UI's own components rendered with sample data by the tests:

    ACCESSION_UI_PREVIEW_DIR=/tmp/previews dotnet test --solution Accession.sln -c Release
    python3 tools/help/make_screenshots.py /tmp/previews

Needs Python Playwright (pip install playwright) and a Chromium; set CHROMIUM to its path if it is not the default below.
"""

import os
import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "src" / "Accession.App" / "wwwroot" / "help" / "images"
CHROMIUM = os.environ.get("CHROMIUM", "/opt/pw-browsers/chromium-1194/chrome-linux/chrome")

# (output file, preview page, element to capture (selector, containing text) or None for the window, window size)
SHOTS = [
    ("start.png", "start", None, (1280, 760)),
    ("dashboard.png", "dashboard-light", None, (1360, 900)),
    ("dashboard-by-media.png", "dashboard-selection", ("section.card", "By media"), (1360, 900)),
    ("dashboard-none-selected.png", "dashboard-none-selected", (".empty-state", None), (1360, 900)),
    ("dashboard-dark.png", "dashboard-dark", None, (1360, 900)),
    ("topbar-matter-link.png", "dashboard-light", (".topbar", None), (1360, 900)),
    ("topbar-read-only.png", "read-only", (".topbar", None), (1360, 900)),
    ("media.png", "media", None, (1360, 900)),
    ("media-detail.png", "media", ("aside.card.detail", None), (1360, 900)),
    ("media-scanning.png", "media-scanning", None, (1360, 900)),
    ("files.png", "files-media-tab", None, (1360, 900)),
    ("files-tree.png", "files-media-tab", (".tree-card", None), (1360, 900)),
    ("files-filters.png", "files-media-tab", (".filter-card", None), (1360, 900)),
    ("files-saved-searches.png", "files-saved-searches", None, (1360, 900)),
    ("saved-search-list.png", "files-saved-searches", (".tree-card", None), (1360, 900)),
    ("saved-search-bar.png", "files-saved-searches", (".bulk-bar", None), (1360, 900)),
    ("categories.png", "categories", None, (1360, 900)),
    ("scan-queue.png", "scan-queue", None, (1360, 900)),
    ("errors.png", "errors", None, (1360, 900)),
    ("audit.png", "audit", None, (1360, 900)),
    ("dialog-new-inventory.png", "form-new-inventory", (".modal", None), (1280, 900)),
    ("dialog-settings.png", "form-settings", (".modal", None), (1280, 900)),
    ("dialog-export.png", "form-export", (".modal", None), (1280, 1000)),
    ("dialog-saved-search.png", "form-saved-search", (".modal", None), (1280, 800)),
]


def main() -> None:
    previews = Path(sys.argv[1] if len(sys.argv) > 1 else os.environ.get("ACCESSION_UI_PREVIEW_DIR", ""))
    if not previews.is_dir():
        sys.exit("Usage: make_screenshots.py <preview folder written by the tests>")

    OUT.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch(executable_path=CHROMIUM)
        for name, page_name, element, (width, height) in SHOTS:
            page = browser.new_page(viewport={"width": width, "height": height}, device_scale_factor=1)
            page.goto((previews / f"{page_name}.html").as_uri())
            page.wait_for_timeout(150)  # fonts and layout
            target = OUT / name
            if element is None:
                page.screenshot(path=str(target))
            else:
                selector, text = element
                locator = page.locator(selector, has_text=text) if text else page.locator(selector)
                locator.first.screenshot(path=str(target))
            page.close()
            print(f"{name:32} <- {page_name}")
        browser.close()


if __name__ == "__main__":
    main()
