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
| FOUND-007 | Recover previous state after restart [4] | 6 | Verified | Displays, screens, assignments and menus are restored after restart (live) |

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
| OUT-001 | Borderless, no chrome, no scrollbars, no controls, fills selected display, covers taskbar [5] | 3 | Implemented | Window rect = display bounds and client area = display size; all 8,294,400 pixels matched on a Windows monitor. Gate 1: the visible upper-left region of the VP2 canvas showed no chrome, but the bottom and right of the canvas lie outside the 1176x672 LED area, so a taskbar or border there cannot be seen on the wall |
| OUT-002 | Canvas uses the actual pixel size of the selected display [7] | 3 | Verified | Gate 1 (user report, 2026-10-07, 7x2 wall = 1176x672, VP2 given 1920x1080, scaling off): the output canvas was the Windows display's actual 1920x1080 and was received by the VP2 as such |
| OUT-003 | Unused canvas is pure black [7] | 3 | Implemented | Interior capture: 8,282,404 of 8,282,404 pixels exactly #000000 on the dev display. Not yet checked on the VP2 |
| OUT-004 | Start / Stop output without closing the app [27] | 3 | Verified | Start, Stop, Start, Stop run twice by self-test and again via the UI; settings file byte-identical afterwards |
| OUT-005 | Output window uses the same renderer as preview [5, 15] | 9 | Verified | Output window shows the same MenuRenderService pages as the preview; live capture equals the renderer PNGs exactly |
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
| SCR-002 | Add / edit / remove / enable-disable screens; assign menus [8] | 4 | Verified | Add, edit, remove, enable/disable verified in Phase 4; assigning a menu through the drop-down verified live in Phase 6 (saved, restored, shown in the Menus tab) |
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
| CAL-001 | "Identify Screens": large screen number, name, W×H on each screen [17] | 5 | Implemented | Identify Screens shows SCREEN, a large number, the name, W x H and X/Y on every drawable screen at its own size. Pixel-tested and captured exactly on a Windows monitor. Gate 1 exercised Screen Calibration and Output Canvas Calibration; Identify Screens itself was not reported on the LED wall |
| CAL-002 | Test pattern: outer 1-px border, corner markers, center crosshair, center lines, grid, number, native size [17] | 5 | Verified | Gate 1 (user report, 2026-10-07, 7x2 wall = 1176x672, VP2 given 1920x1080, scaling off): Screen Calibration at X=0,Y=0, 336x672 showed the complete 1-pixel perimeter, all four corner markers complete, corner coordinates (0,0) (335,0) (0,671) (335,671), center (168,336) correctly placed, grid and geometry consistent across panel boundaries, no clipping, stretching, scaling or offset. Also pixel-tested and captured exactly on a Windows monitor |
| CAL-003 | Crisp 1-px lines, no anti-aliasing [17] | 5 | Verified | Unit tests read back every pattern (boundary is exactly 1 pixel, lines on exact pixels, text from a whole-pixel font) and a capture of the real 3840x2160 monitor matched the composed frame with 0 of 8,294,400 pixels different in every mode. Windows output only; LED behavior is Gate 1 |
| CAL-004 | Markers for the output canvas corners and center to reveal VP2 crop anchoring [D-4] | 5 | Verified | Gate 1 (user report, 2026-10-07, 7x2 wall = 1176x672, VP2 given 1920x1080, scaling off): Output Canvas Calibration revealed the VP2 mapping: top-left anchored, source (0,0) at the top-left LED, the wall shows the upper-left 1176x672 of the 1920x1080 source, no visible scaling or offset, 1-pixel detail crisp |
| CAL-005 | **Gate 1: hardware pixel mapping verified on the real VP2** [32] | 5 | Verified | Gate 1 (user report, 2026-10-07, 7x2 wall = 1176x672, VP2 given 1920x1080, scaling off): passed. See D-27 for the recorded VP2 behavior |
| CAL-006 | Output Canvas Calibration is independent of configured screens and identifies source coordinates so a photo shows the visible source rectangle [D-23] | 5 | Verified | Gate 1 (user report, 2026-10-07, 7x2 wall = 1176x672, VP2 given 1920x1080, scaling off): used to determine the VP2 source rectangle (origin 0,0). Also pixel-tested |
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
| MENU-001 | Menu: ID, Name, Header, Subtitle, Categories, Items, Theme [10] | 6 | Verified | Menu has Id, Name, Header, Subtitle, Logo, Categories, Items and Theme (template id, font, page seconds, page indicator); every field round-trips through its file (unit tests) and survived restarts live |
| MENU-002 | Item: ID, Name, Description, Price (free text), Category, Visible, Sold Out, Featured, Sort Order [10] | 6 | Verified | Item has Id, Name, Description, Price, Category, Visible, Sold Out, Featured, Sort Order; all round-trip exactly, including prices such as "$12 / $16", "Market Price", "3 for $10" and non-ASCII text |
| MENU-003 | Category: ID, Name, Visible, Sort Order [13] | 6 | Verified | Category has Id, Name, Visible, Sort Order; round-trip and ordering tests |
| MENU-004 | Deterministic ordering of items and categories [29] | 6 | Verified | Order is SortOrder then stored position, so it is deterministic; add, move, remove and category moves keep sequences clean (unit tests) |
| MENU-005 | Hidden items are removed and content reflows [12] | 6 | Verified | A hidden item or category produces exactly the page of a menu without it (pixel-identical); unhiding restores the original pages exactly |
| MENU-006 | Sold Out and Hidden are independent states; treatment is not stored in data [12] | 6 | Verified | Sold out and hidden are separate flags; sold-out items stay in the view, flagged; the data holds no treatment (unit tests) |
| MENU-007 | Optional logo per menu, imported into app-managed assets [D-2] | 6 | Verified | Logo chosen through the real file dialog was copied into the app assets folder as a content-hash name, kept after the original was deleted (test), reused when the same picture is imported twice, and restored after restart. PNG/JPEG/BMP/GIF only, decided from content, 20 MB limit. Drawing the logo is Phase 7 |
| MENU-008 | Missing/corrupt logo falls back safely and alerts the operator [22, D-2] | 6 | Verified | Missing logo: menu renders without it, space reclaimed (pixel-identical to a menu with no logo), warning shown on the menu card, preview and output panel. Unreadable logo likewise. Live: deleted the logo file while output ran and the wall and warning updated within a page period |
| MENU-009 | A screen shows a menu by id; assignment is independent of the layout, two screens may share a menu, and a missing menu is flagged but never cleared [D-31] | 6 | Verified | Validator tests; live: assigned, restarted, deleted the menu, the screen was flagged and screens.json kept the id |
| MENU-010 | One file per menu with atomic save, backups and recovery; one damaged file cannot affect the others and is never replaced by an empty menu [D-28] | 6 | Verified | Store tests and live: backed-up menu restored with a notice; a damaged menu with no backup was reported and kept as .corrupt |
| MENU-011 | Deleting a menu moves its file to a trash folder and asks first, naming the screens that use it [D-28, D-31] | 6 | Verified | Store tests; live dialog accepted with Y, file in trash |
| MENU-012 | Menu load problems and recoveries are shown to the operator at startup and in the Menus tab | 6 | Verified | Live: both messages shown in the startup banner |
| MENU-013 | Items may be uncategorized; an item pointing at a missing category is treated as uncategorized and reported [D-29] | 6 | Verified | Ordering, view and validator tests |
| MENU-014 | Menus tab: list, create, create sample, rename, header, subtitle, logo, delete; saved automatically [D-32] | 6 | Verified | Live UI tests. Category and item editing is Phase 8 |
| MENU-015 | A sample "Festival Food" menu (3 categories, 10 items) is available for reviewing templates [D-32] | 6 | Verified | Unit tests and live creation |

## Rendering (RENDER)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| RENDER-001 | One authoritative pipeline for preview and output [15, 28] | 7 | Verified | One pipeline: MenuLayoutEngine then MenuRenderer, called through MenuRenderService by both the LED output and the Menus-tab preview. Live: the real monitor matched the renderer's own PNG pages with 0 differing pixels (4 checks) |
| RENDER-002 | Render at each Screen's native W×H, then place into the canvas [16] | 7 | Verified | Each page is drawn at the screen's own size (336x672 verified; other sizes tested) and placed with ComposeScreens at the screen's X,Y; live capture matched exactly at (0,0) and (336,0) |
| RENDER-003 | 1 rendered pixel = 1 output pixel regardless of Windows DPI scaling [16] | 3 | Implemented | Output window reads its own DPI (WPF 144 = Windows 144 on the dev monitor). Gate 1 mapping was exactly 1:1, but the VP2 monitor scale used was not recorded, and a monitor with different scaling from the operator monitor has not been tested |
| RENDER-004 | Pixel-snapped placement, nearest-neighbor, no blur [16] | 3 | Verified | Gate 1 (user report, 2026-10-07, 7x2 wall = 1176x672, VP2 given 1920x1080, scaling off): pixel-level calibration details remained crisp (no blur from the PC side or the VP2) |
| RENDER-005 | First template tuned for 336×672 portrait; large type, high contrast [14] | 7 | Verified | Gate 2 (user approval, 2026-10-07): the unchanged template was tested on the physical LEDs and approved as tested. Behavior verified by 100+ automated tests and live captures |
| RENDER-006 | Multiple templates possible later [14, 34] | 7 | Implemented | Menu.Theme.TemplateId selects the template; the layout engine and renderer are separate from the template constants; an unknown template falls back with a warning. Only one template exists |
| RENDER-007 | Category headers supported [13] | 7 | Verified | Category headings drawn as full-width amber bars, repeated with (CONT.) when a category continues; hidden categories leave no heading (unit and pixel tests) |
| RENDER-008 | Sold-out treatment chosen by template [12] | 7 | Verified | Gate 2 (user approval, 2026-10-07): the unchanged template was tested on the physical LEDs and approved as tested. Sold-out treatment approved |
| RENDER-009 | Never draw text outside screen bounds [14] | 7 | Verified | Text is measured with the same font engine that draws it; nothing is drawn past the page (every op checked inside the page; pixel scan of margins; drawn ink within measured box) |
| RENDER-010 | Paginate overflow; header/logo repeat; categories not orphaned; recompute on changes [D-3] | 7 | Verified | Pagination from measured heights: items never split, heading never alone at a page bottom, (CONT.) headings, hidden things take no space, header repeated, recalculated on every render. Sweep tests over hundreds of layouts; live: 3-page menu on the real monitor |
| RENDER-011 | Per-menu page duration; rotation timer in output; preview follows [D-3] | 7 | Verified | Per-menu page duration (Theme.PageSeconds, default 10, clamped 2 to 120), one-page menus never rotate; live: pages changed at 2 s, 12 s, 22 s, 32 s (1, 2, 3, back to 1). No editor UI for the duration yet (Phase 8); changing it in the menu file works |
| RENDER-012 | Warn when content cannot fit even paginated; never shrink below minimum size [14, D-3] | 7 | Verified | An item that cannot fit an empty page at the minimum sizes is reported and not drawn (unit and pixel tests); sizes are never reduced to fit more on a page |
| RENDER-013 | Bundled, theme-selectable font loaded from file; fallback with warning [D-2] | 7 | Verified | Lato bundled from files; the menu theme names the family; an unknown family or missing Fonts folder falls back with a warning and still renders (tests). Choosing the font in the UI arrives with the editor |
| RENDER-014 | Last-good frame retained if a render throws; operator alerted [22, D-1] | 7 | Implemented | If drawing a menu throws, the previous picture stays on the wall, the error is logged and the operator is told. Safety net is in place; no failing menu could be provoked to exercise it |
| RENDER-015 | **Gate 2: LED readability confirmed on real panels** [32] | 7 | Verified | **Gate 2 PASSED.** User approved the layout as physically tested on the LED wall |
| RENDER-016 | A logo keeps its proportions, is never stretched, is centered, fits a 320x64 box, and repeats on every page [D-33] | 7 | Verified | Unit tests for wide, tall and square logos; pixel tests measured the drawn logo and its aspect |
| RENDER-017 | With no logo the logo space is reclaimed, not left blank | 7 | Verified | Layout tests (content moves up exactly 70 px) and pixel equality with a never-branded menu |
| RENDER-018 | Each text role has an explicit minimum size; preferred sizes are always used, the minimum only as a last resort for a single item [D-33] | 7 | Verified | Typography tests; test that only an item that cannot otherwise fit is drawn at the minimum |
| RENDER-019 | Featured is a restrained treatment: warm name color and a margin bar, with no change to height, wrapping or pagination [D-33] | 7 | Verified | Gate 2 (user approval, 2026-10-07): the unchanged template was tested on the physical LEDs and approved as tested. Featured treatment approved |
| RENDER-020 | Page number "n/N" at the bottom right on multi-page menus, off by theme; space reserved only when needed | 7 | Verified | Unit tests, pixel tests, live |
| RENDER-021 | Problems (missing/unreadable logo, font or template fallback, item overflow) are shown to the operator on the menu card, the preview and the output panel | 7 | Verified | Live: warning appeared on all three |
| RENDER-022 | Rendering is deterministic: the same menu gives byte-identical pages | 7 | Verified | Tests |
| RENDER-027 | A word in an item name is never cut in the middle when the price could move: if the longest word does not fit beside the price, the price goes to its own row first; a word wider than the whole line is still split [D-41] | 7 | Verified | Core and Rendering tests; Lemonade now draws whole at 168 wide; page counts unchanged |
| RENDER-023 | Menu pages are cached by content signature and re-rendered on any change, including logo file changes | 7 | Verified | Live logo delete changed the wall within one page period |
| RENDER-024 | The renderer works natively at any screen size, including 168x672 (one panel wide): the layout uses the screen's own width, typography is never scaled to fit, and a menu is never a scaled copy of another size [D-39] | 7 | Verified | 22 Core and 15 Rendering tests at 168x672 (plus 1 deliberately skipped test that documents the known mid-word defect); real monitor: two 168-wide screens side by side matched the renderer's PNGs with 0 differing pixels through a full 4-page rotation. Physical LED readability of this size is part of the Gate 2 extension (RENDER-025) |
| RENDER-025 | **Gate 2 extension: readability of the unchanged template on a 168x672 screen (leftmost 1x2 panel column)** [D-39] | 7 | Verified | User approved the unchanged template as tested on the 168x672 column |
| RENDER-026 | Narrow-screen adaptation (never cut a word, price on the description row, compact continuation header) [D-39] | 7 | Withdrawn | The proposed narrow adaptation (compact continuation header, price on description row, portrait-narrow variant) was NOT approved: the user approved the unchanged template. Only the mid-word fix below was made |

## Operator UI (UI)
| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| UI-001 | Operator window with output status chip and normal Windows behavior [5, 25] | 1 | Implemented | Chip shows NO DISPLAY SELECTED / NO DISPLAY / CONFIRM DISPLAY / STOPPED / LIVE. BLACKOUT arrives in Phase 10 |
| UI-002 | Menu selection, item list with Edit / Sold Out / Hide-Show per item [11, 25] | 8 | Verified | Menu tab (the tab shown on start): pick a menu, see its categories and items in order, each with price, Sold Out, Hide/Show and Edit. 31 view-model tests; screenshot; live |
| UI-003 | Quick price editing; add/edit items and categories [11, 24] | 8 | Verified | Price box on every row; Edit section for name, description, featured and category; add and delete items and categories; reorder. View-model tests and live typing of a price |
| UI-004 | Edits update output immediately, no publish step [11] | 8 | Verified | No publish step. Live on the real monitor: after clicking Sold Out, clicking Hide, and typing a price, the wall matched the expected render each time (0 differing pixels in 3 of 3 states) |
| UI-005 | Technical settings kept out of the everyday view [24] | 8 | Implemented | Everyday controls are on the first tab, which is selected on start; displays, screens and the menu library are on separate tabs. Whether this is simple enough for event staff is a judgement for the user |
| UI-006 | Live preview using the shared renderer [15] | 9 | In Progress | Menus tab has a preview of the exact pages the LED shows; the full live preview of the output canvas is Phase 9 |
| UI-007 | Scaled preview and 100% pixel preview [15] | 9 | In Progress | Menus-tab preview at 1x (true pixels), 2x or 3x with nearest-neighbor scaling and page stepping; whole-canvas preview is Phase 9 |
| UI-008 | Operator-visible warnings for overflow / missing assets / errors [14, 22] | 8 | Verified | Missing logo, font fallback and items that cannot fit are shown at the top of the editor and on the Menu Library card, in the preview, and in the output panel |
| UI-009 | Keyboard shortcuts documented in the UI [26] | 10 | Not Started | |
| UI-010 | Startup notice banner when data was recovered or reset | 1 | Implemented | Banner bound to load result; shown path verified by unit-level store tests, UI display not yet exercised |
| UI-011 | Hide/Show and Sold Out are one-press toggles with clear state (button text, red Sold Out button, HIDDEN / SOLD OUT / FEATURED tags, dimmed hidden rows); hidden items stay listed so they can be shown again | 8 | Verified | View-model tests and screenshot |
| UI-012 | Categories can be added, renamed, hidden, reordered and deleted; deleting a category with items asks whether to delete the items too or keep them with no category [D-42] | 8 | Verified | View-model tests |
| UI-013 | Items can be added (opened for typing at once), renamed, described, featured, moved up or down, moved to another category, and deleted after confirmation | 8 | Verified | View-model tests |
| UI-014 | A save failure is shown to the operator and the edit stays on screen; the next successful save clears it | 8 | Verified | View-model test |
| UI-015 | Seconds per page (2 to 120) and page numbers are editable per menu in the Menu Library | 8 | Implemented | Built; not exercised in the live UI. Range is enforced and tested at the clock level |
| UI-016 | The editor never rebuilds its lists while the operator is typing or toggling, so focus is not lost | 8 | Verified | View-model test (toggle leaves the same row objects) |

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
| PERSIST-005 | Persist settings, display config, screens, menus, categories, items, assignments, theme [19] | 6 | Verified | Settings, display choices, screens, screen-to-menu assignments, menus, categories, items and theme all persist and restore (unit tests and live restarts) |
| PERSIST-006 | Schema version in every file; backup before migration [D-1] | 6 | In Progress | Every file carries a schema version and a newer file is refused and kept. Backing up before a migration is not built because no migration exists yet; it will be added with the first schema change |
| PERSIST-007 | Autosave: discrete actions immediate, text edits debounced ~500 ms, flush on exit [21] | 8 | Verified | Quick actions save at once; typed text saves once after a 500 ms pause (burst of 6 keystrokes = 1 write); focus leaving a box, switching menu or tab, and program exit flush anything waiting. 9 Core and 10 view-model tests; live: price stayed old on disk while typing and became the new value after the pause; typed text then window closed at once was saved |
| PERSIST-008 | Assets copied into app-managed folder [20] | 6 | Verified | Imported images live in %APPDATA%\LedMenu\assets; only a plain file name is stored in a menu; names that could point elsewhere are refused |

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


## Backlog: Video Screen / Playlist (VID) — post-menu, NOT scheduled, NOT started
Recorded at the user's request as a future feature only. Nothing here is implemented, and the current menu work was deliberately not refactored around it. To be revisited after the planned menu phases are complete.

| ID | Requirement | Phase | Status | Evidence |
|---|---|---|---|---|
| VID-001 | A configured Screen can show either Menu content or Video/Playlist content | Backlog | Not Started | |
| VID-002 | Initial intended use: Screen 1 = 168x672 Menu, Screen 2 = 168x672 Video Playlist | Backlog | Not Started | |
| VID-003 | Video uses local media files only; operation never depends on the Internet | Backlog | Not Started | |
| VID-004 | Import local video files (into an app-managed media folder, like logo assets) | Backlog | Not Started | |
| VID-005 | Multiple videos in a playlist | Backlog | Not Started | |
| VID-006 | Reorder the playlist | Backlog | Not Started | |
| VID-007 | Enable / disable individual videos | Backlog | Not Started | |
| VID-008 | Automatic continuous playback and looping | Backlog | Not Started | |
| VID-009 | Manual Previous / Next controls | Backlog | Not Started | |
| VID-010 | Mute / audio controls; muted playback likely the default | Backlog | Not Started | |
| VID-011 | Fit / Fill behavior for media whose aspect ratio differs from the Screen | Backlog | Not Started | |
| VID-012 | Native 168x672 media renders without scaling | Backlog | Not Started | |
| VID-013 | Video is strictly clipped to its assigned Screen rectangle and never affects adjacent Screens | Backlog | Not Started | |
| VID-014 | Menu rendering and video playback on separate Screens operate simultaneously | Backlog | Not Started | |
| VID-015 | Missing or corrupt media fails safely without affecting other Screens | Backlog | Not Started | |

Note: spec section 33 originally listed video playback under "do not add unless explicitly requested". This is that explicit request, scheduled after the menu phases.

## Out of scope (spec section 33)
Cloud, auth, remote admin, Firebase, web hosting, SaaS, sync, ordering, payments, inventory, POS, free-form drag-and-drop design, decorative animation, AI content. (Video was requested as a post-menu feature; see the VID backlog above.) Page rotation (RENDER-011) is functional, not decorative.
