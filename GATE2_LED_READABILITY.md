# Gate 2 — Physical LED readability of the first menu template

> **Extension:** the intended menu screen may be 168 × 672 (one panel column). See `GATE2_168x672_TEST.md` for testing the unchanged template on exactly that region.

> **Status: PASSED (user approved the layout as tested on the LED wall).** See D-41. Kept for re-testing other walls or menus.

The first template (`portrait-basic`) is built for the 2×2 panel screen, **336 × 672** LED pixels. The desktop preview helps you check content, but it **cannot** tell you whether the type is readable on the wall. Please judge from the real LEDs, at your real viewing distance.

Gate 1 already established that a 336×672 Screen at X=0, Y=0 fills the leftmost 2×2 panel region exactly, so these steps reuse that setup.

## 1. Set up (about 5 minutes)

1. Start the app with the VP2 connected and set up as in Gate 1 (VP2 selected as LED Output, Windows signal 1920×1080, VP2 scaling off).
2. **Screens** tab: make sure there is one Screen at **X=0, Y=0, Width=336, Height=672**, enabled. (If it is gone, click **Add Screen (336×672)**; its first spot is 0,0.)
3. **Menus** tab: under **Add a sample**, click **Festival Food**, **Dense Menu** and **Sold Out Demo** (and **Long Text Stress** if you want to see the worst case).
4. Open **Preview** on any menu card to see exactly the pages the LED will show. Zoom 1× is true pixel size.
5. On the **Screens** tab, use the **Menu** drop-down on your Screen to pick the menu you want to test. The LED changes immediately when output is running.
6. Click **Start Output** with **Normal Output** selected.

Pages of a menu that needs more than one page rotate every **10 seconds** (page 1 → 2 → 3 → back to 1) with a small `2/3` page number at the bottom right. A menu that fits on one page does not rotate.

## 2. What to look at on the wall

Please check each sample and take photos at your typical viewing distance, plus one close-up.

| Test | Menu to choose | What it shows |
|---|---|---|
| 1. Normal page | **Festival Food** | A realistic one-page menu: title, subtitle, three categories, descriptions, prices (`$12`, `$6 / $9`, `3 for $10`, `Market Price`), one featured item (Black & Blue Burger) |
| 2. Pagination | **Dense Menu** | 22 items on 3 pages, a category continuing across pages (`SIDES (CONT.)`), the page number, and the 10-second rotation |
| 3. Sold Out | **Sold Out Demo** | Three sold-out items (Veggie Burger, Soft Pretzel, Onion Rings) and one featured item (Wings) |
| 4. Worst case | **Long Text Stress** | Long title, long names, long descriptions, long prices, a sold-out item with a long name |
| 5. Branding layout (optional) | any | Click **Set Logo...** on a menu card and choose `samples\placeholder-logo.png` (a neutral stand-in, not real branding) to see the layout with a logo; use **Remove Logo** afterwards |

No real branding is built in. Without a logo the space is used for content.

## 3. The template's exact sizes (in LED pixels)

| Element | Size | Weight | Color | Smallest allowed (last resort) |
|---|---|---|---|---|
| Menu title | 28 | bold | white | 22 |
| Subtitle | 15 | regular | light gray | 13 |
| Category bar | 18 | bold | black on amber bar | 16 |
| Item name | 20 | bold | white (featured: warm yellow) | 18 |
| Price | 20 | bold | amber | 18 |
| Description | 14 | regular | gray | 13 |
| SOLD OUT badge | 12 | bold | white on red | 11 |
| Page number | 12 | regular | gray | — |

- Margins are 8 pixels left and right and 6 at top and bottom. The font is Lato.
- The preferred size is always used. The smaller "last resort" size is used only for a single item that would not otherwise fit on an empty page. If it still cannot fit, it is reported and not drawn (never cut off).
- Text edges are smoothed (anti-aliased) by the font engine, so letters are not pure on/off LEDs.
- The title, optional logo and subtitle repeat on every page.

## 4. Treatments to judge

- **Sold Out:** name dimmed to gray and struck through, description darker, and the price replaced by a red **SOLD OUT** badge. The item stays in place.
- **Featured:** warm yellow name plus a short amber bar in the left margin. No size or position change.
- **Category bar:** full-width amber bar with black text; `(CONT.)` is added when a category continues on the next page.

## 5. Questions I would like answered

Please answer from the wall, not from the preview. Short notes are fine.

1. **Distance:** from how far away can you read (a) the title, (b) category bars, (c) item names and prices, (d) descriptions, (e) the SOLD OUT badge, (f) the page number? What is your target viewing distance at the festival?
2. **Type sizes:** is anything too small? Is anything wastefully large? (Descriptions at 14 and the page number at 12 are the likeliest too-small candidates.)
3. **Smoothing:** do the smoothed letter edges look good, or would pure on/off (crisp) letters read better?
4. **Hierarchy:** do names and prices stand out clearly over descriptions? Do category bars separate the sections well without taking too much height?
5. **Colors and contrast:** is the amber/white/gray on black readable and comfortable? Are the dimmed sold-out items still legible enough, but clearly unavailable?
6. **Sold Out:** obvious from several feet away? Chaotic when several are sold out?
7. **Featured:** noticeable but restrained? Too subtle?
8. **Spacing:** are items too tight or too loose? Is there enough margin at the panel edges (nothing visually cut)?
9. **Pagination:** is 10 seconds per page right? Is the `2/3` page number useful, too small, or unwanted? Is repeating the title on every page good, or would you prefer a smaller header on later pages?
10. **Logo (if tried):** is the logo area (up to 320 × 64) the right size? A menu that just fits on one page without a logo may spill onto a second page with one, which is expected.
11. Anything that looks wrong or surprising.

Send photos (whole wall and close-ups) with your notes. Tuning is easy; nothing here is final.

## 6. Advanced (optional, by editing a menu file)

Until the Phase 8 editor, a menu's **page time** and **font** can be changed by hand in its file, `%APPDATA%\LedMenu\menus\<id>.json`, under `"Theme"`: `"PageSeconds": 10` (allowed 2 to 120), `"FontFamily": "Lato"`, `"ShowPageIndicator": true`. To use another font, copy its font files into the `Fonts` folder next to the program and put its family name in `FontFamily`. If the font is not found the app falls back to Lato and shows a warning. Make sure a font's license allows you to use it this way.

## 7. After the test

Report your answers; I will adjust the template accordingly. Phase 8 (operator menu editing) does not start until you approve the physical LED result.
