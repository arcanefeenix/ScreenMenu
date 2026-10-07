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
| 8 Operator Menu UI | Not started | |
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

## Phases 8–13
As in the spec (sections 31 and 32): operator menu UI, live preview (scaled and 100%), blackout and shortcuts, multiple screens, hardening (monitor disconnect, corrupt-file drills, missing assets, log review, stress cases), and a self-contained folder publish with operator documentation and a clean-machine test.

## Backlog (post-menu, not scheduled): Video Screen / Playlist
Recorded as requirements VID-001 to VID-015 and decision D-40. A Screen would show either Menu or Video/Playlist content (first use: Screen 1 = 168x672 Menu, Screen 2 = 168x672 Video Playlist), using local media only, with import, multi-video playlists, reorder, enable/disable, looping, Previous/Next, mute (muted by default), Fit/Fill, native-size playback without scaling, strict clipping to the Screen rectangle, simultaneous operation with menu screens, and safe failure on missing or corrupt media. **Not started and not in the current phase plan.** To be revisited after Phase 13 or when the user asks.

