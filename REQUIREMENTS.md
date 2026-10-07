# REQUIREMENTS

Source: `MASTER_PROMPT.md` (section numbers in brackets). Approved scope changes are in `DECISIONS.md` and marked **[D-n]**.

Status values: **Not Started** → **In Progress** → **Implemented** (code exists) → **Verified** (evidence that it works).
"Verified" requires evidence listed in the Evidence column. Hardware-dependent items stay Implemented until a human gate passes.

## Foundation (FOUND)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| FOUND-001 | Native Windows desktop app: C# / .NET / WPF [4] | 1 | Verified | Solution builds on .NET 8 SDK, 0 warnings (-warnaserror); app launches |
| FOUND-002 | Fully offline; no cloud, web server, Firebase or network dependency [4] | 1 | Verified | No network packages referenced; only test packages beyond the SDK |
| FOUND-003 | Minimal external dependencies [4] | 1 | Verified | App projects have zero NuGet packages |
| FOUND-004 | Clean separation: Models, Persistence, Menu mgmt, Screen/output mgmt, Rendering, UI/VMs, Display discovery [28] | 1 | In Progress | Core / Persistence / Rendering / App projects created; remaining areas arrive with their phases |
| FOUND-005 | Automated test projects for non-UI logic [29] | 1 | Verified | Core.Tests (8) and Persistence.Tests (12) pass |
| FOUND-006 | Self-contained publish, no installer [D-5] | 13 | Not Started | |
| FOUND-007 | Recover previous state after restart [4] | 6 | Not Started | |

## Display discovery (DISP)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| DISP-001 | Enumerate Windows displays [6] | 2 | Verified | Real PC: 2 displays listed with friendly name, ID, position; log shows detection |
| DISP-002 | Operator selects LED Output Display and Operator Display [6] | 2 | Implemented | LED output selection verified end to end (select, persist, restart). Operator selection implemented and unit-tested but not exercised in the live UI; it is a remembered label only, the window is not moved (D-10) |
| DISP-003 | Detect each display's pixel resolution and DPI [7, 16] | 2 | Implemented | Real PC: 3840×2160 at 150% shown for both. Verified only with identical scaling on both monitors; mixed-DPI hardware check still outstanding (conversion logic is unit-tested) |
| DISP-004 | Persist selection with enough identity to restore it (device path, bounds fallback) [6] | 2 | Verified | 14 unit tests on matching/persistence; live restart restored LED output as "OUTPUT: STOPPED / LED OUTPUT" |
| DISP-005 | If saved output display is missing: output disabled, operator alerted, can choose another; never auto-substitute [6, 22] | 2 | Verified | Start with a saved display that is not connected: refused with a clear message, no output window created, no other display used, saved identity unchanged |
| DISP-006 | Operator always retains control [6] | 2 | Verified | Operator window stayed operable and could always stop output |
| DISP-007 | Warn and require confirmation before choosing the operator's own / primary / only display as output [D-1] | 2 | Verified | Policy unit tests; live test: choosing the primary display raised the confirmation dialog and saved nothing while it was open |
| DISP-008 | Identify Displays: temporary large number + name on each monitor (non-fullscreen, no focus steal, auto-closes) | 2 | Verified | Live test: 640×360 label centered on both monitors including the one at negative coordinates, closed itself after 4 s |
| DISP-009 | UI shows number, name, resolution, scaling, position, Windows name, device ID, and tags WINDOWS PRIMARY / OPERATOR DISPLAY / OPERATOR WINDOW IS HERE / LED OUTPUT | 2 | Verified | Screenshot of the running app |
| DISP-010 | Re-scan automatically when monitors are connected/disconnected (debounced) | 2 | Implemented | Hooked to the Windows display-change event; not exercised because no monitor could be unplugged during testing |
| DISP-011 | A display whose Windows device ID changed but which has the same name, size and position is offered only after the operator confirms it [D-11] | 2 | Implemented | Matcher unit tests (single match, ambiguity, different resolution); confirm button not exercised live |

## Output window (OUT)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| OUT-001 | Borderless, no chrome, no scrollbars, no controls, fills selected display, covers taskbar [5] | 3 | Implemented | Window rect = display bounds and client area = display size; all 8,294,400 pixels matched a known pattern, so no title bar, border, rounded corner or taskbar covers it. Verified on the 4K/150% dev monitors only, not on the VP2 |
| OUT-002 | Canvas uses the actual pixel size of the selected display [7] | 3 | Implemented | Window sized from the display actually selected (3840x2160 here); not yet seen on a 1920x1080 / VP2 signal |
| OUT-003 | Unused canvas is pure black [7] | 3 | Implemented | Interior capture: 8,282,404 of 8,282,404 pixels exactly #000000 on the dev display. Not yet checked on the VP2 |
| OUT-004 | Start / Stop output without closing the app [27] | 3 | Verified | Start, Stop, Start, Stop run twice by self-test and again via the UI; settings file byte-identical afterwards |
| OUT-005 | Output window uses the same renderer as preview [5, 15] | 9 | Not Started | |
| OUT-006 | Monitor disconnect: no crash, operator notified, no hijacking another display, reconnects only to the same display [22] | 12 | In Progress | Output stops (not redirected) if the display disappears, and repositions if its size changes; logic written but not exercised because no monitor could be unplugged |
| OUT-007 | Output never steals keyboard focus from the operator window [27, D-1] | 3 | Verified | Output window IsActive=false after start, the operator window stayed foreground; Esc sent while focus was elsewhere did nothing |
| OUT-008 | Start is refused unless the saved display is matched by device ID and connected right now; never redirected [D-14] | 3 | Verified | Unit tests (8) and live missing-display test |
| OUT-009 | Output window never rounds corners or draws a DWM border; sits topmost over the taskbar | 3 | Verified | Full-screen pixel comparison had 0 mismatches including corner and edge pixels |
| OUT-010 | Operator-window-on-output-display warning is repeated at start, with a No default [D-14] | 3 | Verified | Dialog appeared; No cancelled; Yes started; hotkey recovered |
| OUT-011 | Esc stops output only when the output window itself has focus (it is never focused automatically) [D-14] | 3 | Verified | Esc did nothing with focus elsewhere; stopped output after the output window was focused |
| OUT-012 | Closing the operator window ends the app, so a fullscreen output cannot be orphaned | 3 | Implemented | ShutdownMode=OnMainWindowClose; not exercised |

## Screens (SCR)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| SCR-001 | Screen has ID, Name, X, Y, Width, Height, Enabled, Assigned Menu [8] | 4 | Verified | Model has Id, Name, X, Y, Width, Height, Enabled, AssignedMenuId; round-trip persistence test and live restart |
| SCR-002 | Add / edit / remove / enable-disable screens; assign menus [8] | 4 | In Progress | Add, edit, remove (with confirmation), enable/disable verified live. Assigning a menu arrives with menus (Phases 6 and 8); the AssignedMenuId is preserved and shown |
| SCR-003 | Numeric X/Y/W/H editing is authoritative [8] | 4 | Verified | Typed values saved exactly; drag results saved as whole pixels; an invalid or too-large entry is shown only while editing and reverts to the stored value when the box loses focus (verified live) |
| SCR-004 | Visual scaled canvas with screen rectangles [8] | 4 | Verified | Screenshot: whole 3840x2160 canvas drawn to scale with numbered, named, colored rectangles |
| SCR-005 | Draggable/resizable rectangles with snapping, if practical [8] | 4 | Verified | Live mouse test: move (672 to 1201), resize edge (336 to 601 wide), snap back to neighbour edge, clamp to 0,0 and to canvas bottom-right (ended exactly at 3840x2160); Alt turns snapping off (not exercised) |
| SCR-006 | Validation: negative coordinates, invalid size, outside canvas [7] | 4 | Verified | Unit tests (non-positive, negative, outside canvas incl. 1-pixel overshoot and int overflow); live flags shown; invalid screens are kept, not corrected |
| SCR-007 | Overlap detection warns but does not forbid [7] | 4 | Verified | Unit tests (overlap by area, edge-touching is fine, disabled ignored); live warnings on both screens and cleared by disabling one |
| SCR-008 | Panel resolution (168×336) not hard-coded [2] | 4 | Verified | Tests use 1x1, 1176x336, 620x1080, 920x1080 and 336x672 screens; 336x672 appears only as the Add default and a convenience button |
| SCR-009 | Layouts saveable as presets; menus independent of layouts [9] | 4 | In Progress | Layout is a named, versioned object separate from menus and from the selected display. Saving several presets is not built yet |
| SCR-010 | Output / Screen / Menu remain separate concepts [3] | 4 | Verified | Screens live in screens.json; editing, moving and removing screens left settings.json (selected displays) byte-identical |
| SCR-011 | Multiple independent screens, each with its own menu, on one canvas [2, 31] | 11 | Not Started | |
| SCR-012 | A screen outside the canvas, or made invalid by a smaller output, is flagged and left exactly as entered (never clipped or resized) [D-17] | 4 | Verified | Unit tests; live: display missing with last-known 1920x1080 flagged a 2976-wide edge and screens.json stayed byte-identical |
| SCR-013 | Disabled screens are excluded from overlap checks and from `RenderableScreens`; an enabled screen with errors is excluded from rendering [D-17] | 4 | Implemented | Unit-tested; no screen rendering exists until Phase 5 and 7 |
| SCR-014 | Canvas for validation = connected output size, else last known size, else "unknown" (bounds not checked) [D-17] | 4 | Verified | Resolver unit tests; live: connected, missing display and none selected |
| SCR-015 | Screen layout persists in its own file with atomic save, backups and recovery | 4 | Verified | Persistence tests (round trip, out-of-range values, corrupt-file recovery); live restart restored layout |
| SCR-016 | Add Screen defaults to 336x672 at the first free position; a per-screen "Set 336x672" button; neither is a model constraint | 4 | Verified | Placement unit tests; live adds landed at (0,0), (336,0), (672,0) |
| SCR-017 | Removing a screen asks for confirmation; No leaves the layout and file unchanged | 4 | Verified | Live test with N then Y |

## Calibration (CAL)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| CAL-001 | "Identify Screens": large screen number, name, W×H on each screen [17] | 5 | Implemented | Identify Screens shows SCREEN, a large number, the name, W x H and X/Y on every drawable screen at its own size. Pixel-tested and captured exactly on a Windows monitor; not yet seen on the LED wall (Gate 1) |
| CAL-002 | Test pattern: outer 1-px border, corner markers, center crosshair, center lines, grid, number, native size [17] | 5 | Implemented | Per-screen: 1-px boundary, 20x20 corner markers, center lines, 16/64-px grid, circle, diagonals, number, name, W x H, output X/Y, local corner coordinates. Pixel-tested; not yet seen on the LED wall (Gate 1) |
| CAL-003 | Crisp 1-px lines, no anti-aliasing [17] | 5 | Verified | Unit tests read back every pattern (boundary is exactly 1 pixel, lines on exact pixels, text from a whole-pixel font) and a capture of the real 3840x2160 monitor matched the composed frame with 0 of 8,294,400 pixels different in every mode. Windows output only; LED behavior is Gate 1 |
| CAL-004 | Markers for the output canvas corners and center to reveal VP2 crop anchoring [D-4] | 5 | Implemented | Output Canvas Calibration: boundary, edge bands, rulers, 100-px grid with x,y labels, nine named anchors with coordinates, center lines on the middle pixels, circles and 1-px detail blocks. Pixel-tested and captured exactly on a Windows monitor; VP2 behavior is Gate 1 |
| CAL-005 | **Gate 1: hardware pixel mapping verified on the real VP2** [32] | 5 | In Progress | Phase 5 tools are built and documented in GATE1_VP2_CHECKLIST.md. Waiting for the physical VP2 test; nothing is verified until the results are reported |
| CAL-006 | Output Canvas Calibration is independent of configured screens and identifies source coordinates so a photo shows the visible source rectangle [D-23] | 5 | Implemented | Unit tests: nine anchors with exact coordinates, labels from a real font at every 100 px, ruler numbers, crop simulation still shows 40+ labels; ignores screens. LED reading is Gate 1 |
| CAL-007 | Operator can choose Normal Output, Identify Screens, Screen Calibration or Output Canvas Calibration, before or during output | 5 | Verified | Live: all four selected with the mouse; frames matched the real screen in each mode |
| CAL-008 | Test modes are obvious in the operator UI (magenta banner and "TEST" on the status chip) and never silent | 5 | Verified | Live: banner before start, banner while live, chip text per mode |
| CAL-009 | Test modes never modify screen, menu or display configuration | 5 | Verified | screens.json and settings.json byte-identical after running every mode |
| CAL-010 | Invalid enabled screens are not drawn in test patterns and are listed to the operator; disabled screens are left out; overlapping screens are drawn [D-24] | 5 | Verified | Plan unit tests; live: "Too Far" and "Zero" listed with reasons, "Switched Off" omitted |
| CAL-011 | Stopping output always returns to Normal Output, so a test pattern cannot return by surprise [D-24] | 5 | Verified | Live: mode reset after Ctrl+Shift+F12 |
| CAL-012 | Identify Screens returns to Normal Output automatically after 15 seconds [D-24] | 5 | Verified | Live: banner and chip back to normal after 16 s |
| CAL-013 | Every pattern is generated at its own native pixel size with integer coordinates, no anti-aliasing and no WPF DPI dependence; per-screen patterns are not scaled from a generic image | 5 | Verified | 100+ unit assertions including sizes 1x1 to 3840x2160; frame equals the screen pattern pixel for pixel |
| CAL-014 | Calibration frames are shown 1:1 on the output window and reproduce exactly at 3840x2160 on a 150% monitor at negative desktop coordinates | 5 | Verified | Capture comparison: 0 mismatches for Identify, Screen Calibration, Canvas Calibration and Normal |
| CAL-015 | Offline reference and self-test tools: `--dump-calibration` writes the patterns as PNG; `--selftest-output` checks every mode on the real screen [D-25] | 5 | Verified | Both run and produced the results in this report |

## Menu data (MENU)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| MENU-001 | Menu: ID, Name, Header, Subtitle, Categories, Items, Theme [10] | 6 | Not Started | |
| MENU-002 | Item: ID, Name, Description, Price (free text), Category, Visible, Sold Out, Featured, Sort Order [10] | 6 | Not Started | |
| MENU-003 | Category: ID, Name, Visible, Sort Order [13] | 6 | Not Started | |
| MENU-004 | Deterministic ordering of items and categories [29] | 6 | Not Started | |
| MENU-005 | Hidden items are removed and content reflows [12] | 6 | Not Started | |
| MENU-006 | Sold Out and Hidden are independent states; treatment is not stored in data [12] | 6 | Not Started | |
| MENU-007 | Optional logo per menu, imported into app-managed assets [D-2] | 6 | Not Started | |
| MENU-008 | Missing/corrupt logo falls back safely and alerts the operator [22, D-2] | 6 | Not Started | |

## Rendering (RENDER)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| RENDER-001 | One authoritative pipeline for preview and output [15, 28] | 7 | Not Started | |
| RENDER-002 | Render at each Screen's native W×H, then place into the canvas [16] | 7 | Not Started | |
| RENDER-003 | 1 rendered pixel = 1 output pixel regardless of Windows DPI scaling [16] | 3 | Implemented | Output window reads its own DPI (WPF 144 = Windows 144 on the output monitor). Both dev monitors are 150%, so a DPI different from the operator monitor has not been exercised on hardware; conversion maths unit-tested |
| RENDER-004 | Pixel-snapped placement, nearest-neighbor, no blur [16] | 3 | Implemented | Probe bitmap drawn 1:1 with nearest-neighbor and pixel snapping reproduced exactly in a screen capture; menu bitmaps use the same host in Phase 7 |
| RENDER-005 | First template tuned for 336×672 portrait; large type, high contrast [14] | 7 | Not Started | |
| RENDER-006 | Multiple templates possible later [14, 34] | 7 | Not Started | |
| RENDER-007 | Category headers supported [13] | 7 | Not Started | |
| RENDER-008 | Sold-out treatment chosen by template [12] | 7 | Not Started | |
| RENDER-009 | Never draw text outside screen bounds [14] | 7 | Not Started | |
| RENDER-010 | Paginate overflow; header/logo repeat; categories not orphaned; recompute on changes [D-3] | 7 | Not Started | |
| RENDER-011 | Per-menu page duration; rotation timer in output; preview follows [D-3] | 7 | Not Started | |
| RENDER-012 | Warn when content cannot fit even paginated; never shrink below minimum size [14, D-3] | 7 | Not Started | |
| RENDER-013 | Bundled, theme-selectable font loaded from file; fallback with warning [D-2] | 7 | Not Started | |
| RENDER-014 | Last-good frame retained if a render throws; operator alerted [22, D-1] | 7 | Not Started | |
| RENDER-015 | **Gate 2: LED readability confirmed on real panels** [32] | 7 | Not Started | Human verification required |

## Operator UI (UI)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| UI-001 | Operator window with output status chip and normal Windows behavior [5, 25] | 1 | Implemented | Chip shows NO DISPLAY SELECTED / NO DISPLAY / CONFIRM DISPLAY / STOPPED / LIVE. BLACKOUT arrives in Phase 10 |
| UI-002 | Menu selection, item list with Edit / Sold Out / Hide-Show per item [11, 25] | 8 | Not Started | |
| UI-003 | Quick price editing; add/edit items and categories [11, 24] | 8 | Not Started | |
| UI-004 | Edits update output immediately, no publish step [11] | 8 | Not Started | |
| UI-005 | Technical settings kept out of the everyday view [24] | 8 | Not Started | |
| UI-006 | Live preview using the shared renderer [15] | 9 | Not Started | |
| UI-007 | Scaled preview and 100% pixel preview [15] | 9 | Not Started | |
| UI-008 | Operator-visible warnings for overflow / missing assets / errors [14, 22] | 8 | Not Started | |
| UI-009 | Keyboard shortcuts documented in the UI [26] | 10 | Not Started | |
| UI-010 | Startup notice banner when data was recovered or reset | 1 | Implemented | Banner bound to load result; shown path verified by unit-level store tests, UI display not yet exercised |

## Operational controls (OPS)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| OPS-001 | Blackout: all output pure black, operator stays live, state preserved, toggle off restores [18] | 10 | Not Started | |
| OPS-002 | Blackout shortcut Ctrl+Shift+B [18, D-6] | 10 | Not Started | |
| OPS-003 | Operator UI clearly shows blackout is active [18] | 10 | Not Started | |
| OPS-004 | Identify shortcut Ctrl+Shift+I [26, D-6] | 10 | Verified | Ctrl+Shift+I toggles Identify Screens (confirmed live: banner and chip changed, returned to Normal after 15 s) |
| OPS-005 | Stop Output shortcut Ctrl+Shift+F12 closes the output without quitting [27, D-6] | 10 | Verified | Global Ctrl+Shift+F12 stopped output with another app focused and when output covered the operator window |
| OPS-006 | App always starts live, not blacked out [D-5] | 10 | Not Started | |
| OPS-007 | Never create a fullscreen state the operator cannot leave [27] | 10 | Verified | Output on the same display as the operator window: warning shown, then Ctrl+Shift+F12 recovered; also Stop button and Esc on the focused output window |
| OPS-008 | Output status chip: LIVE / STOPPED / BLACKOUT / NO DISPLAY [D-1] | 10 | In Progress | LIVE, STOPPED, NO DISPLAY done in Phase 3; BLACKOUT with Phase 10 |

## Persistence (PERSIST)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| PERSIST-001 | JSON files in an application-data directory, not the exe folder [19] | 1 | Verified | settings.json created under %APPDATA%\LedMenu on first launch; AppPaths tests |
| PERSIST-002 | Atomic save: temp file, validate, replace [19] | 1 | Verified | JsonFileStore tests incl. leftover .tmp and invalid-save refusal. Real power-loss not testable; logic verified |
| PERSIST-003 | Keep backups of last known-good files (last 10) [19, D-1] | 1 | Verified | Backup creation and pruning tests |
| PERSIST-004 | Corrupt file: recover from backup, keep corrupt copy, never silently reset [22] | 1 | Verified | Recovery tests (corrupt, missing, invalid, no backup) |
| PERSIST-005 | Persist settings, display config, screens, menus, categories, items, assignments, theme [19] | 6 | In Progress | Settings and screen layout persisted (screens.json); menus, categories, items and themes arrive in Phase 6 |
| PERSIST-006 | Schema version in every file; backup before migration [D-1] | 6 | In Progress | AppSettings has schema version and rejects newer files |
| PERSIST-007 | Autosave: discrete actions immediate, text edits debounced ~500 ms, flush on exit [21] | 8 | Not Started | |
| PERSIST-008 | Assets copied into app-managed folder [20] | 6 | Not Started | |

## Reliability and logging (REL / LOG)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| LOG-001 | Log startup/shutdown, display events, config load/recovery, assignment changes, renderer failures [23] | 1 | In Progress | Startup, settings load/recovery, display detection, output selected/lost/regained logged (changes only, no flooding); assignments and renderer failures arrive later |
| LOG-002 | Log is useful for event troubleshooting; no per-frame noise [23] | 1 | Implemented | Daily rolling file, 14-day retention |
| LOG-003 | Logging never throws into the app [22] | 1 | Verified | FileLog test with unwritable directory |
| REL-001 | Invalid config: no crash, explain, prevent invalid rendering [22] | 12 | Not Started | |
| REL-002 | Unhandled UI exception is logged and the operator window survives [22, D-1] | 1 | Implemented | Handler installed; not yet provoked in a test |
| REL-003 | Single instance only [D-1] | 1 | Implemented | Second launch shows "already running" dialog in smoke test |
| REL-004 | Startup self-check logs display geometry and DPI [D-1] | 3 | Verified | Every start logs Windows bounds, window rectangle, client size, WPF DPI, monitor DPI and whether they agree |
| REL-005 | Stress / edge testing list from spec section 29 [29] | 12 | Not Started | |

## Packaging (PKG)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| PKG-001 | Self-contained release build, runnable from a folder/desktop [D-5] | 13 | Not Started | |
| PKG-002 | Clean-machine startup test [31] | 13 | Not Started | Human verification required |
| PKG-003 | Operator documentation [31] | 13 | Not Started | |

## Out of scope (spec section 33)
Cloud, auth, remote admin, Firebase, web hosting, SaaS, sync, ordering, payments, inventory, POS, free-form drag-and-drop design, video, decorative animation, AI content. Page rotation (RENDER-011) is functional, not decorative.
