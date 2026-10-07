# IMPLEMENTATION PLAN

Each phase ends with: relevant tests, full solution build, updated REQUIREMENTS / DECISIONS / this file, an Implemented-vs-Verified report, one focused git commit, and a list of anything needing human verification. Human gates are not passed without approval.

Commands: `dotnet build LedMenu.sln -warnaserror` and `dotnet test LedMenu.sln`.

| Phase | Status | Gate |
|---|---|---|
| 1 Foundation | **Complete** | |
| 2 Display Discovery | **Complete** (review requested) | |
| 3 Output Window | **Complete** (review requested) | |
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
