# IMPLEMENTATION PLAN

Each phase ends with: relevant tests, full solution build, updated REQUIREMENTS / DECISIONS / this file, an Implemented-vs-Verified report, one focused git commit, and a list of anything needing human verification. Human gates are not passed without approval.

Commands: `dotnet build LedMenu.sln -warnaserror` and `dotnet test LedMenu.sln`.

| Phase | Status | Gate |
|---|---|---|
| 1 Foundation | **Complete** | |
| 2 Display Discovery | **Complete** (review requested) | |
| 3 Output Window | **Complete** (review requested) | |
| 4 Screen Model | **Complete** (review requested) | |
| 5 Calibration | **Complete** | **Gate 1 PASSED (user-verified)**: VP2 top-left anchored, 1:1, 336x672 Screen at 0,0 verified |
| 6 Menu Data (incl. asset import for logos) | **Complete** (review requested) | |
| 7 Menu Renderer (template, pagination, fonts, logo) | **Complete** | **Gate 2 PASSED (user approved the layout as tested on the LED wall, including 168x672)** |
| 8 Operator Menu UI | **Complete** (review requested) | |
| 9 Live Preview | Not started | |
| 10 Blackout and Operational Controls | Not started | |
| 11 Multiple Screens | Not started | |
| 12 Hardening | Not started | |
| 13 Packaging | Not started | Clean-machine test |

## Phase 1 — Foundation (complete)
**Scope:** solution and projects, .NET 8 pin, local settings with atomic save/backup/recovery, logging, operator shell, test projects, tracking documents.
**Requirements:** FOUND-001..005, PERSIST-001..004, LOG-001..003, UI-001, UI-010, REL-002, REL-003.
**Done:** `Core` (log, `AppSettings`), `Persistence` (`AppPaths`, `JsonFileStore<T>`), `App` (shell, single instance, exception handlers, PerMonitorV2 manifest), 20 passing tests.
**Not done in this phase:** the exception handler and the recovery banner have not been exercised in the live UI.

## Phase 2 — Display Discovery (complete)
**Scope:** enumerate monitors through Win32 (`EnumDisplayMonitors`, per-monitor DPI, display-configuration API for friendly name and stable device path), choose Operator and LED Output displays, persist identity, handle a missing output safely, warn on risky choices, Identify Displays, automatic re-scan.
**Requirements:** DISP-001..011.
**Done:** `Core/Display` (`DisplayInfo`, `DisplayIdentity`, `DisplayMatcher`, `SelectionPolicy`), `App/Display` (`Win32DisplaySource`, `IdentifyWindow`), `DisplaysViewModel`, Displays screen with identity details and tags, status chip wired to display state.
**Tests:** 25 new unit tests (matching, renumbering, ordering, DPI scale, selection policy, settings round trip, Phase 1 file compatibility), plus live checks on this two-monitor PC.
**Not verified:** mixed-DPI hardware (both monitors are 150%), live hot-plug re-scan, Operator selection in the UI, the confirm-fallback button.
**Deferred:** moving the operator window to the chosen Operator display (D-10); Identify Screens belongs to Phase 5.

## Phase 3 — Output Window (complete)
**Scope:** borderless output window placed in physical pixels on the chosen display, pure black, Start/Stop, no focus stealing, global Ctrl+Shift+F12, geometry logged on every start, status chip wired to real state, diagnostic self-test.
**Requirements:** OUT-001..004, OUT-006..012, RENDER-003, RENDER-004, REL-004, OPS-005, OPS-007, DISP-005, DISP-006.
**Tests:** 25 new unit tests (start policy, geometry comparison, DPI conversions) plus the live and self-test evidence recorded in the Phase 3 report.
**Not verified:** a DPI different from the operator monitor's (both dev monitors are 150%); display unplugged while output runs; the real Mirackle VP2 (resolution 1920x1080 / 1176x672, crop anchor); output on a display with Windows scaling other than 150%.
**Gate note:** nothing here counts as VP2 pixel-mapping verification; that remains Gate 1 after Phase 5.

## Phase 4 — Screen Model (complete)
**Scope:** `Screen` and `ScreenLayout` model, validation against the output canvas, numeric editor, scaled canvas editor with optional drag/resize/snap, persistence in `screens.json`, add/remove/enable, default 336x672 convenience.
**Requirements:** SCR-001..008, SCR-010, SCR-012..017; SCR-002 and SCR-009 partly (menu assignment and multiple presets come later); SCR-011 waits for Phase 11 rendering.
**Tests:** 48 new unit tests (validator, layout file rules, canvas resolver, placement, snapping, persistence) plus live UI checks recorded in the Phase 4 report.
**Not verified:** hot display change while the Screens tab is open; very many screens; Alt-to-disable-snap; non-numeric text recovery looks (the box keeps the typed text with a red border while the saved value is unchanged).
**Not in this phase:** drawing screens on the LED output (Phase 5 and 7), menu assignment UI, saving several layout presets.

## Phase 5 — Calibration (complete; Gate 1 passed)
**Scope:** Identify Screens, per-screen Screen Calibration, whole-canvas Output Canvas Calibration, four selectable output modes with an unmistakable banner, crisp pixel-exact patterns, pixel read-back tests, Gate 1 instructions. Also the Phase 4 numeric-field revert.
**Requirements:** CAL-002, CAL-003, CAL-004, CAL-005, CAL-006, CAL-007..015, OPS-004, SCR-003 Verified. CAL-001 (Identify Screens) Implemented: not reported on the LED wall.
**Tests:** 51 new unit tests plus real-screen capture comparison of every mode and live UI checks.
**Gate 1 result (user-verified):** the VP2 is top-left anchored and 1:1 with scaling off; the wall shows the upper-left 1176x672 of a 1920x1080 source; a 336x672 Screen at (0,0) maps exactly to the leftmost 2x2 panel region with full perimeter, corners and center correct. Recorded as D-27.

## Phase 6 — Menu Data (complete)
**Scope:** menu, category and item model with ordering, visibility, sold out and featured; the rendered-view rules (`MenuView`); file-per-menu persistence with backups and recovery; image import for logos; menu-to-screen assignment; schema versions; a small Menus tab.
**Requirements:** MENU-001..004, 006, 007, 009..015 Verified; MENU-005 and MENU-008 Implemented (the drawing half is Phase 7); PERSIST-005, PERSIST-008, FOUND-007, SCR-002 Verified; PERSIST-006 In Progress.
**Tests:** 84 new unit tests (ordering, editor operations, view rules, validator, library, assignment, asset rules, file store, asset store) plus live UI and corruption tests.
**Not done in this phase:** editing categories and items in the UI, autosave debounce (Phase 8); schema migration with a pre-migration backup (first needed when the schema changes); drawing a menu or logo (Phase 7).

## Phase 7 — Menu Renderer (complete; Gate 2 passed)
**Scope:** the first 336x672 portrait template, layout and pagination from measured text, bundled font, logo handling, sold-out and featured treatments, one renderer for output and preview, operator problem reporting, rotation of pages, samples for the LED test.
**Requirements:** RENDER-001, 002, 007, 009..013, 016..018, 020..023, MENU-005, MENU-008, OUT-005 Verified; RENDER-005, 006, 008, 014, 019 Implemented (look and readability wait for Gate 2); RENDER-015 In Progress; UI-006, UI-007 In Progress.
**Tests:** 125 new automated tests: 78 in Core (text wrapping, page clock, typography, layout, logo box, sold out, featured, long content, pagination boundaries and orphan sweeps, overflow) and 47 with the real font and pixel read-back in the new Rendering.Tests project. Live: the real monitor matched the renderer's PNGs with 0 differing pixels on four captures, pages rotated on the 10 s schedule, and a deleted logo file produced the warning and reclaimed space.
**Gate 2 (human):** follow `GATE2_LED_READABILITY.md`; photos and notes on type sizes, spacing, hierarchy, sold out, featured, pagination and viewing distance. Phase 8 does not start until you approve the physical LED result.
**Gate 2 extension (168x672):** the unchanged template was tested natively at 168x672 and a physical test of the leftmost 1x2 panel column is prepared in `GATE2_168x672_TEST.md`; a narrow-screen adaptation is proposed but not built (D-39). **Open design questions for Gate 2:** smoothed versus crisp letters; whether descriptions (14 px) and the page number (12 px) are large enough; whether the header should shrink on later pages; page time; logo box size.

## Phase 8 — Operator Menu UI (complete)
**Scope:** menu selection, quick price editing, Sold Out, Hide/Show, add/edit/delete/reorder items and categories, move between categories, autosave with a typing debounce, warnings in the editor, page time and page numbers per menu.
**Requirements:** UI-002, 003, 004, 008, 011..014, 016 and PERSIST-007 Verified; UI-005 and UI-015 Implemented.
**Tests:** 40 new automated tests: 9 for the autosave debouncer (Core) and 31 for the editor view-models (new `LedMenu.App.Tests` project). Live on the real monitor: Sold Out, Hide and a typed price each changed the wall to exactly the expected render (0 differing pixels in 3 of 3), the price was not written to disk until typing paused, and typing then closing the window at once was still saved.
**Not in this phase:** drag-and-drop reordering (up/down buttons instead), choosing the template or font in the UI, keyboard shortcuts documentation (Phase 10), the full live preview of the output canvas (Phase 9).

## Phase 9 — Live Preview (complete)
**Scope:** one shared frame builder for LED output and preview, preview panel beside the tabs (whole output fitted; any screen at 100%/200%/300%/400%), LIVE/STOPPED badge, preview kept current when output is stopped.
**Requirements:** UI-006 and UI-007 Implemented (not yet viewed by a person in the running app).
**Tests:** 44 new automated tests: PreviewMath (Core), PreviewViewModel (7), and the scaled control rendered offscreen and compared pixel by pixel (100% = 0 differing pixels; 300% = exact 3x3 blocks; fit fills the box). Full suite 504 passing, build with warnings as errors clean. A test caught a real bug: a moved screen kept its old rectangle in the preview.
**Not verified:** a screenshot of the running window could not be captured in this session, so the panel layout was not eyeballed. Human check: start the app, open Screens, add a 336x672 screen, pick it in the preview and confirm 100%.
**Not in this phase:** blackout and shortcut documentation (Phase 10).

## Phase 10 — Blackout and Operational Controls (complete)
**Scope:** Blackout (button, Ctrl+Shift+B, banner, chip, preview badge), Help tab documenting all shortcuts, output status chip BLACKOUT.
**Requirements:** OPS-006 Verified; OPS-001, 002, 003, 008 and UI-009 Implemented (hardware and human checks pending).
**Tests:** 7 new automated tests (6 blackout rules in Core, 1 preview badge). Full suite 511 passing; build with warnings as errors clean. The live self-test gained a blackout section.
**Not verified:** the real-screen pixel capture returned one flat colour for every pixel in this session (the same failure that blanked window screenshots), so the live pixel comparisons, old and new, could not be used. Human check: start output on the LED, press Ctrl+Shift+B (with the operator window focused) and confirm the wall goes pure black and returns; confirm the red banner and chip; stop while blacked out and start again.
**Not in this phase:** nothing from the spec for Phase 10 remains open apart from the human checks.

## Phase 11 — Multiple Screens (complete)
**Scope:** prove and pin down multiple screen definitions, independent menu assignments and several rendered regions on one canvas. The screen editor, per-screen menu assignment and multi-screen drawing already existed from Phases 4, 6 and 7; this phase added the tests that make them dependable.
**Requirements:** SCR-011 Implemented (needs a look on the LED with two screens).
**Tests:** 9 new automated tests (real renderer, 2 to 4 screens). Full suite 520 passing; build with warnings as errors clean. No application code changed.
**Not verified:** two screens on the physical LED wall. Human check: add a second 168x672 screen next to the first (Screens tab), give it a different menu (Menu Library or the screen's menu box), start output and confirm each shows its own menu and pages rotate independently.

## Phase 12 — Hardening (complete)
**Scope:** audit of spec sections 22 and 29, then closing the gaps: per-screen render failure isolation, monitor disconnect rules with safe auto-restore, error-dialog storm protection, autosave retry, file-system failure drills, stress and hostile-input tests, a logging review.
**Requirements:** SCR-011, LOG-001, REL-001, REL-005, REL-006, REL-007, REL-008 Verified; OUT-006 Verified on hardware by the user (unplug and replug); REL-002 Implemented.
**Tests:** 65 new automated tests (11 display-watch rules, 5 dialog throttle, 16 file-system drill cases, 28 stress and hostile-input renders, 2 render-failure isolation, 3 autosave retry). Full suite 585 passing; build with warnings as errors clean. Startup smoke test: no errors or warnings in the log.
**Changes to the program:** renderer failures isolated per screen; output restarts when its own display returns; failed saves retry; dialog throttling; menu-render problems now also reach the log. The renderer and the persistence layer needed no changes: they passed every hostile case on the first run.
**Not verified:** a repeating unhandled exception on screen (REL-002). The monitor unplug and replug (OUT-006) was confirmed by the user.

## Phase 13 — Packaging (complete)
**Scope:** release build, packaging, clean-start test, operator documentation.
**Requirements:** PKG-001, PKG-002 and PKG-003 Verified (the user ran the package on a clean machine and approved the guide). OPS-002, 004 and 005 Verified by the user.
**Delivered:** `tools\publish.ps1` (publish, add documents and samples, check required files, zip, smoke test); `docs\OPERATOR_GUIDE.html`; `docs\READ_ME_FIRST.txt`; data-folder override `LEDMENU_DATA_DIR`; single-instance lock per data folder; stale 'later phase' wording removed from the Menu Library tab.
**Tests:** 5 new (data-folder override and instance key). Full suite 590 passing in Release; build with warnings as errors clean. Smoke test of the published copy: passed.
**Clean-machine check (passed, user, 2026-10-07):** copy `dist\LedMenuControl-0.1.0.zip` to such a PC, unzip it anywhere (the desktop is fine), double-click `LedMenu.App.exe`, and follow `OPERATOR_GUIDE.html` section 2 with the real wall. Windows SmartScreen may warn about an unsigned program the first time (More info, Run anyway); signing is not done.
**Not in this phase:** an installer, a program icon, code signing, auto-update.

## Video Screen / Playlist (approved 2026-10-07; in progress)
Decoder decided by feasibility test (VIDEO_FEASIBILITY.md, D-49). Steps, each ending with a stop for review:
1. **Data and import (done).** Screen content kind and playlist model, screens.json schema 2 with backup-before-migration, media folder and `MediaStore` with Windows-decoder test-open, validator warnings (empty playlist, missing media, size mismatch). 60 new tests (Core 27, Persistence 25, real decoder 8); full suite 650 passing in Release; build with warnings as errors clean. Live: a v1 screens file was upgraded by the real app with the original kept. Not yet verified with the user's own video (no UI to import it yet).
2. **Playback engine (done).** `PlaylistSequencer`, `VideoPlacement`, `VideoLevels` in Core; `VideoScreenPlayer` (two decoders, early load, 31 ms picture grabs of the screen rectangle only, failure and stall handling) in Rendering. 61 new tests (Core 45, real decoder 16 incl. 80-test App suite run 12 times with no failure); full suite 711 passing in Release; warnings-as-errors build clean. Real clips: your three videos looped for 5 minutes headless (tools\VideoSoak): 31.8 pictures/s, median gap 31 ms, 0 black pictures, 6% of one core, memory flat at about 165 MB after start-up, managed heap saw-tooth 1-17 MB (no leak). Defects found and fixed by testing: black flash between videos (first picture of a new decoder), a one-frame blank/garbage picture 40-50 ms into a video, black blip at a clip's very end, and a 21/s picture rate from timer granularity. Not yet: nothing is drawn on the LED output (step 3), no operator controls (step 4); there is no audio by decision (D-51); an exact-duration reader for import (Windows reports whole seconds) is still to do in step 4.
3. **Output integration (done).** `VideoCompositor` and `RegionMath` (Core), `FrameSurface` (persistent output bitmap with partial refresh), `VideoScreenHost` (one player per video screen), wired into `OutputViewModel`, the preview (Revision / PictureChanged) and app start/exit. 50 new tests (Core 19, App 15 plus the changed fakes); full suite 746 passing in Release; the App suite run 6 more times with no failure; warnings-as-errors build clean. **End-to-end on the real program** (`tools\video-e2e.ps1`, which seeds an isolated data folder, runs the real output window on the second display with the Dense Menu on one 168x672 screen and your three clips on another, and has the program compare what the output window really holds with the picture it believes it is showing): 29 of 29 comparisons exact, 16-18 different video pictures seen, never black; blackout on = window completely black, off = exact again; Screen Calibration pattern exact; back to Normal the video resumes; after Stop the video screens are black. Why this way: screen capture is not usable in this session, so the program reads back its own window surface instead. Not yet: an operator screen to choose Menu or Video and edit playlists (step 4); the physical LED check (step 5).
4. **Operator screen (done).** `Mp4Info` (true length), `PlaylistEditorViewModel`/`VideoEditingServices`, the Menu/Video choice on `ScreenItemViewModel`, `IVideoTransport` on the host, desk "Preview videos", "Remove unused video files", Screens-tab XAML, operator guide section 4 and read-me. 38 new tests (Core 12, App 26); full suite 784 passing in Release; App suite 5 more runs without failure; warnings-as-errors build clean. **Real operator window driven through UI Automation** (`tools\video-ui-smoke.ps1`, your three clips): 14 of 14 checks (editor present, three videos listed with true lengths, Preview videos, Next/Pause/Resume/Previous reading back "Playing 2 of 3" and so on, switching Menu/Video and back keeps the playlist, no errors in the log). Not exercised by any test: the Windows file-open dialog (please click Add videos once), and the up/down/remove buttons through the real window (their logic is covered by view-model tests). Next: step 5, the physical LED check and a long soak.
5. Hardware check with a 168x672 menu screen and a 168x672 video screen together, then a long soak.

Original backlog record follows.

## Backlog (post-menu, not scheduled): Video Screen / Playlist
Recorded as requirements VID-001 to VID-015 and decision D-40. A Screen would show either Menu or Video/Playlist content (first use: Screen 1 = 168x672 Menu, Screen 2 = 168x672 Video Playlist), using local media only, with import, multi-video playlists, reorder, enable/disable, looping, Previous/Next, mute (muted by default), Fit/Fill, native-size playback without scaling, strict clipping to the Screen rectangle, simultaneous operation with menu screens, and safe failure on missing or corrupt media. **Not started and not in the current phase plan.** To be revisited after Phase 13 or when the user asks.

