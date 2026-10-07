# Gate 1 — Mirackle VP2 hardware pixel-mapping check

> **Status: PASSED (user-verified, 2026-10-07).** Result: with a 1920×1080 Windows signal and VP2 scaling disabled, the VP2 is top-left anchored and maps source (0,0) to the top-left LED at 1:1. The 7×2 (1176×672) wall shows the upper-left 1176×672 of the source. A 336×672 Screen at X=0, Y=0 filled exactly the leftmost 2×2 panel region with a complete perimeter, corners and center. See D-27 in DECISIONS.md. This checklist is kept for re-running the test with other walls or controllers.

Purpose: find out exactly how the VP2 turns the Windows/HDMI picture into LEDs, and set up the 336×672 Screen from facts, not guesses.

Nothing in this document assumes what the VP2 does. The app makes no assumption either: Screen 1 is **not** assumed to belong at (0,0).

Until you report results, nothing about VP2 behavior or hardware pixel mapping is marked Verified. Everything the app did on ordinary Windows monitors (exact pixels, 0 mismatches) says only that the **PC side is exact**.

## Before you start

- Run the app from `src\LedMenu.App\bin\Debug\net8.0-windows\LedMenu.App.exe` on the PC that will drive the VP2.
- Dim the room if you can. Set the wall to a normal, fixed brightness. Do not change brightness between photos.
- Keep a phone or camera ready. Use the same camera position for the whole-wall shot of each pattern.
- **Safety:** if the picture ever covers the operator window, press **Ctrl+Shift+F12** to stop output. **Esc** also stops output if you click the output window first. Output never moves to another display.
- Pattern notes:
  - **Identify Screens** switches back to Normal Output by itself after 15 seconds. Use **Screen Calibration** or **Output Canvas Calibration** for photographs; those stay until you change them.
  - Stopping output always returns the mode to Normal Output.
  - None of the test modes change your saved screens or settings.

---

## Step 1 — Windows display configuration

1. Connect the PC's HDMI output to the VP2 input.
2. Open **Windows Settings → System → Display**.
3. Click the monitor that is the VP2 (use Windows' own **Identify** button if unsure).
4. Set:
   - **Multiple displays:** *Extend these displays* (not Duplicate, not "Show only on …"). Do **not** make the VP2 the main display.
   - **Display resolution:** **1920 × 1080**.
   - **Refresh rate:** 60 Hz (or whatever the VP2 documents as its input).
   - **Scale:** 100% on the VP2 is simplest. Other values work, but start with 100%.
   - **Orientation:** Landscape.
5. Keep your normal monitor(s) as they are.

## Step 2 — Select the VP2 as LED Output

1. Start the app and open the **Displays** tab.
2. Find the card that is the VP2. It should say **1920×1080**. Its name may be generic. Use **Identify Displays**; a small numbered label appears on each monitor. (On the LED wall you may see only part of that label. That is expected and is useful information; mention it in your report.)
3. Click **Use as LED Output** on that card. If the app shows a warning, read it. It should not warn if the VP2 is not your primary display and your operator window is elsewhere.
4. The header should now read **OUTPUT: STOPPED** (no red "no display" alert).

## Step 3 — Confirm the Windows / HDMI input resolution

Write down all of these:

- The app card: resolution, "Windows scaling" %, desktop position, device ID.
- Windows **Settings → System → Display → Advanced display** for the VP2: *Active signal mode* (should be 1920 × 1080) and refresh rate.
- If the VP2 shows its detected input resolution anywhere (front panel or its software), write that too.

If any of these is not 1920×1080, stop and fix it before continuing. Every coordinate in this checklist assumes a 1920×1080 canvas.

## Step 4 — Disable VP2 scaling

I do not know the exact menu names on the Mirackle VP2, so I cannot give you a precise path. Use the VP2's own controls or control software and the VP2 manual.

1. Find the setting that controls how the input picture is fitted to the LED wall. Typical names are scaling, zoom, stretch, fit, full screen, or point-to-point / 1:1. Switch scaling **off** (1:1 / point-to-point).
2. Write down the exact name of the option and the value you chose, and the value it had before.
3. If you cannot find such an option, leave the VP2 as it is and say so in your report. The calibration patterns will still reveal what it is doing.

## Step 5 — Run Output Canvas Calibration first

1. In the app, under **Output shows:** click **Output Canvas Calibration**. A magenta banner appears ("Test mode selected…").
2. Click **Start Output**. The banner turns into "TEST PATTERN ON THE LED OUTPUT…" and the header shows `OUTPUT: LIVE — TEST: OUTPUT CANVAS CALIBRATION`.
3. Wait a few seconds for the wall to settle.

What the pattern contains (source coordinates are pixels from the top-left of the 1920×1080 picture):

- A pure 1-pixel white boundary all round, then 4-pixel colored bands: **red = top, blue = bottom, green = left, yellow = right**.
- Rulers on all four edges: a tick every 10 px, longer every 50, longest every 100, with numbers every 100.
- A grid line every 100 px, labeled `x,y` at every intersection (double-size labels every 200 px).
- Nine named anchors: **TOP LEFT, TOP CENTER, TOP RIGHT, CENTER LEFT, CENTER, CENTER RIGHT, BOTTOM LEFT, BOTTOM CENTER, BOTTOM RIGHT**, each with its source coordinate (0,0 / 960,0 / 1919,0 / 0,540 / 960,540 / 1919,540 / 0,1079 / 960,1079 / 1919,1079).
- Magenta center lines on pixels 959–960 across and 539–540 down.
- Two circles centered on the canvas.
- Three 1-pixel detail blocks under the center: **1PX COLUMNS, 1PX ROWS, 1PX CHECKER**.

To keep a reference, you can write the pattern to PNG files: `LedMenu.App.exe --dump-calibration C:\some\folder 1920x1080`.

## Step 6 — What to photograph and report

Take these photos with Output Canvas Calibration on the wall:

1. **Whole wall**, straight on, entire wall in the frame.
2. **Four close-ups, one per wall corner** (top-left, top-right, bottom-left, bottom-right): close enough that the nearest numbers and labels are readable.
3. **Center of the wall**, close enough to read the labels and see the 1PX blocks.
4. Anything odd: repeated numbers, missing numbers, blurry areas, a visible seam.

Report these (a plain list is fine):

- a. Is the white 1-pixel boundary visible on each of the four sides? (top / bottom / left / right)
- b. Which colored bands can you see? (red top, blue bottom, green left, yellow right)
- c. Which of the nine anchor labels can you read?
- d. At the **top-left LED** of the wall: the nearest ruler number or grid label, and how many LEDs it is from the corner.
- e. At the **bottom-right LED** of the wall: the same.
- f. The number of LEDs between two neighboring vertical grid lines (100 source pixels apart), and between two neighboring horizontal grid lines. (1:1 mapping means **100**.)
- g. Do the 1PX COLUMNS / ROWS / CHECKER blocks show clean alternating on/off LEDs, or gray/smeared/irregular?
- h. Do the circles look round?
- i. The wall size you counted: LEDs across and LEDs down (expected 7×2 panels = 1176 × 672).

## Step 7 — Work out the crop origin and the visible source rectangle

Let the wall be `Wled × Hled` LEDs (for example 1176 × 672).

**Origin (x0, y0)** = the source pixel shown at the wall's top-left LED.

- If the first vertical grid line you can see is labeled `X` and it is `n` LEDs from the left edge of the wall, then `x0 = X − n` (this works because 1:1 mapping puts grid lines 100 LEDs apart).
- Do the same with a horizontal grid line for `y0`.
- Visible rectangle = from `(x0, y0)` to `(x0 + Wled − 1, y0 + Hled − 1)`.

Compare with the usual possibilities (1920×1080 input, 1176×672 wall):

| What you find | Origin (x0, y0) |
|---|---|
| Anchored top-left | (0, 0) |
| Centered | ((1920−1176)/2, (1080−672)/2) = **(372, 204)** |
| Anchored bottom-right | (744, 408) |
| Something else | whatever the labels say; report it |

How to read the other effects:

| You see | It means |
|---|---|
| Grid lines exactly 100 LEDs apart, 1PX blocks crisp, numbers run consecutively | **True 1:1 mapping** (with a crop if the whole canvas is not visible) |
| White border and all nine anchors visible on a wall smaller than the canvas, grid lines closer than 100 LEDs (about 61 across and 62 down for 1920×1080 → 1176×672) | **Scaling** (the whole picture squeezed to fit). Scaling is not off yet |
| Some anchors/border missing, but numbers are consecutive and 100 LEDs apart | **Cropping.** Normal if scaling is off. Your origin is the answer to Step 7 |
| Border/bands visible on some edges but not others | **Offset mapping:** the visible window does not start at (0,0) and is not centered |
| The outer 1–4 pixels of an edge that should be visible are missing (a band colored thinner than 4, ticks missing at the edge) | **Overscan/cropping at that edge**; count how many source pixels are missing |
| Ruler numbers or ticks repeat or skip; grid spacing alternates 99/101 LEDs; the 1PX COLUMNS or ROWS block shows doubled or missing stripes | **Missing or duplicated rows/columns** |
| Labels read backwards or in the wrong corner | Mirroring or rotation in the VP2 |

If you want a clean no-crop comparison, you may optionally set Windows to the wall's exact size (1176×672) as an extra test and rerun this step; this is not required.

## Step 8 — Configure the 336×672 Screen

1. In the app, stop output (**Stop Output** or Ctrl+Shift+F12), then open the **Screens** tab.
2. Click **Add Screen (336×672)**, or edit an existing Screen. 336×672 is the size of the 2×2-panel wall (each panel 168×336).
3. Decide where the 2×2 wall is within what the VP2 shows:
   - Look at the **top-left LED of the 2×2 wall** in your Step 6 photos. The source coordinate shown there is the Screen's **X and Y**.
   - If your wall is the 7×2 and the 2×2 section is its left 336 columns, then `X = x0` and `Y = y0` (the origin you found). If the section is elsewhere, add its offset inside the visible wall.
   - Type X and Y into the numeric fields (they are authoritative). Width 336, Height 672.
4. The Screens tab should show no red or amber warning for this Screen.
5. Optional but very informative: add a second Screen of **1176 × 672** at `(x0, y0)` to test the whole 7×2 wall at once. Disable it again afterwards if you do not want it.

## Step 9 — Run Screen Calibration

1. Under **Output shows:**, click **Screen Calibration**, then **Start Output** (or just click the mode while output is running).
2. A calibration pattern appears **inside each enabled, valid Screen**, at that Screen's own size and position. Everything else on the canvas stays black. Screens that are invalid are not drawn, and the app lists them in a red note.
3. Take photos: the whole wall, then close-ups of each of the Screen's four corners and its center.

## Step 10 — Verify the entire 336×672 Screen boundary and pixel mapping

Check each item and report pass/fail with a photo:

- **Outer boundary:** a 1-LED-wide white line is visible on **all four sides** of the Screen, with nothing cut off and no gap between the line and the edge of the Screen's panels.
- **Corner markers:** four filled squares, **red top-left, green top-right, blue bottom-left, yellow bottom-right**, each 20 × 20 LEDs, complete (no missing rows or columns).
- **Corner coordinates** (small text at the corners): `0,0`, `335,0`, `0,671`, `335,671`.
- **Center lines:** two LEDs wide, crossing at the middle: label near the center reads `168,336`.
- **Grid:** faint lines every 16 LEDs (21 columns across, 42 rows down) and brighter every 64.
- **Circle:** round, centered.
- **Text:** the Screen number is large; below it the name, `336 X 672`, and `OUTPUT X … Y …` (the Screen's position in the output).
- **Size check:** counting LEDs across and down gives 336 and 672 (use the grid: 21 × 16 and 42 × 16).
- **No spill:** nothing from the Screen's pattern shows outside the 2×2 wall, and nothing is cut off at its edge.

If the boundary is clipped, shifted, or stretched, the Screen's X/Y (or the VP2 mapping) is not right yet. Change X or Y by the number of LEDs the pattern is off, and run Screen Calibration again.

---

## Report back

Please send:

1. Steps 1–4: Windows settings, the app card values, and the exact VP2 scaling option and value.
2. Step 6: the photos and your answers a–i.
3. Step 7: your calculated origin and visible rectangle, and which row of the tables applies.
4. Steps 8–10: the Screen X/Y/W/H you used, the photos, and pass/fail for each check.
5. Anything unexpected (flicker, delay, wrong colors, a seam between panels).

After that I will mark the Gate 1 requirements as Verified or open a fix, and only then continue to Phase 6 (menu data).
