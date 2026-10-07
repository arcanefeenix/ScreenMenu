# IMPLEMENTATION PLAN

Each phase ends with: relevant tests, full solution build, updated REQUIREMENTS / DECISIONS / this file, an Implemented-vs-Verified report, one focused git commit, and a list of anything needing human verification. Human gates are not passed without approval.

Commands: `dotnet build LedMenu.sln -warnaserror` and `dotnet test LedMenu.sln`.

| Phase | Status | Gate |
|---|---|---|
| 1 Foundation | **Complete** | |
| 2 Display Discovery | Not started | |
| 3 Output Window | Not started | |
| 4 Screen Model | Not started | |
| 5 Calibration | Not started | **Gate 1: VP2 hardware pixel mapping** |
| 6 Menu Data (incl. asset import for logos) | Not started | |
| 7 Menu Renderer (template, pagination, fonts, logo) | Not started | **Gate 2: LED readability** |
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

## Phase 2 — Display Discovery
**Scope:** enumerate monitors (Win32 `EnumDisplayMonitors`, device path), per-monitor DPI, select Operator and Output displays, persist identity, safe handling of a missing output, warn when output equals the operator display.
**Requirements:** DISP-001..007.
**Completion:** displays listed with resolution and DPI in the UI; selection persists across restart; missing display leaves output disabled with a visible alert.
**Tests:** display matching (device path, then bounds fallback), selection persistence, missing-display decision logic, using an injectable display source.

## Phase 3 — Output Window
**Scope:** borderless output window placed in physical pixels on the chosen display, black canvas, start/stop, non-focus-stealing, DPI self-check logged, status chip wired to real state.
**Requirements:** OUT-001..004, OUT-007, RENDER-003, RENDER-004, REL-004, UI-001.
**Completion:** output opens exactly over the selected display at its pixel size; closes without ending the app; pixel read-back test proves one DIP-independent pixel maps to one output pixel at 100%/125%/150%.
**Tests:** coordinate conversion at several DPI scales, bitmap read-back of a test render.

## Phase 4 — Screen Model
**Scope:** `Screen`, `ScreenLayout`, validation (bounds, negative, size, overlap warning), add/edit/remove/enable, numeric editor, scaled canvas view with rectangles, layout presets in the data model; draggable handles only if reliable.
**Requirements:** SCR-001..010.
**Tests:** bounds, negative values, zero size, partially outside, overlap, preset serialization.

## Phase 5 — Calibration
**Scope:** Identify Screens, test pattern (1-px border, corners, crosshair, center lines, grid, number, W×H), output-canvas corner/center markers, crisp integer-coordinate drawing.
**Requirements:** CAL-001..004.
**Tests:** pixel read-back (border at x=0 and x=W-1, center lines, no partially covered pixels).
**Gate 1:** I will give exact instructions for the VP2 test and wait for your report on border visibility, cropping, stretching, centering, dimensions and crop anchor. Phase 6 does not start without your approval.

## Phase 6 — Menu Data
**Scope:** Menu / Category / MenuItem models, ordering, visibility, sold-out, featured, JSON persistence per menu, asset import for logos, menu-to-screen assignment, schema versioning.
**Requirements:** MENU-001..008, PERSIST-005, PERSIST-006, PERSIST-008, FOUND-007.
**Tests:** ordering, hidden category, all hidden, all sold out, serialization round trip, missing asset.

## Phase 7 — Menu Renderer
**Scope:** `ScreenRenderer` producing a native-size bitmap, first 336×672 template, bundled font loading, logo header, category headers, sold-out treatment, pagination and page timer, fit warnings, last-good-frame protection.
**Requirements:** RENDER-001, 002, 005..014.
**Tests:** layout tests for empty menu, one item, long name, long description, long price, many items, hidden category, all sold out, page splitting and orphan-header rules.
**Gate 2:** physical LED readability test; template stays provisional until you give feedback or photos.

## Phases 8–13
As in the spec (sections 31 and 32): operator menu UI, live preview (scaled and 100%), blackout and shortcuts, multiple screens, hardening (monitor disconnect, corrupt-file drills, missing assets, log review, stress cases), and a self-contained folder publish with operator documentation and a clean-machine test.
