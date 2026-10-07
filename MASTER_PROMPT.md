# LED Menu Display — Master Project Specification

## 1. Project Overview

Build a Windows desktop application for operating digital menus on modular LED panel walls at festivals, bars, pop-up events, and similar environments.

The application will run on a Windows PC connected via HDMI to a Mirackle VP2 LED video controller.

The VP2 should receive a normal Windows display signal, initially expected to be 1920×1080. We are **not** attempting to make Windows output the exact native resolution of an individual LED wall.

Instead, the application must create a borderless fullscreen output window on the Windows display connected to the VP2. Within that output canvas, the application will render one or more independently configurable rectangular LED "screens."

Each screen has an exact pixel position and size within the larger output canvas.

Example:

    Windows / HDMI Output: 1920×1080

    Screen 1:
        Name: Food Menu
        X: 0
        Y: 0
        Width: 336
        Height: 672

    Screen 2:
        Name: Drink Menu
        X: 336
        Y: 0
        Width: 336
        Height: 672

The VP2 will be responsible for mapping these regions to the appropriate physical LED panels.

The application must render these regions at their configured pixel dimensions. Do not design the application around scaling a conventional 1920×1080 menu down to the LED wall.

---

# 2. Current Hardware

LED controller:

    Mirackle VP2

Current physical installation:

    7 panels wide
    2 panels high

Current VP2 output resolution:

    1176×672

Therefore each individual LED panel appears to have a native resolution of:

    168×336 pixels

This should be treated as the current known panel resolution, but **do not hard-code it throughout the application**.

The application should allow screen dimensions to be configured independently so that other LED panels/configurations can be supported later.

Initial intended festival configuration:

    2 panels wide
    2 panels high

Resulting LED screen:

    336×672 pixels

This is a portrait 1:2 aspect ratio.

Future configuration:

Two independent 2×2 LED walls may be used simultaneously, for example:

    Screen 1 → Food
    Screen 2 → Drinks

The architecture must support this later even if the initial implementation begins with one screen.

---

# 3. Primary Design Principle

There are three distinct concepts that MUST remain separate:

## Output

The Windows display connected to the LED controller.

Example:

    Mirackle VP2
    1920×1080

## Screen

A physical LED wall represented by a rectangular region within the Output.

Example:

    Left LED Wall
    X: 0
    Y: 0
    Width: 336
    Height: 672

## Menu

The content displayed on a Screen.

Example:

    Festival Food Menu

A Menu can be assigned to a Screen.

Do NOT permanently couple menu content to physical screen configuration.

This should allow:

    Screen: Left LED Wall
    Content: Musikfest Food Menu

to later become:

    Screen: Left LED Wall
    Content: Garlic Fest Menu

without changing the physical screen configuration.

---

# 4. Technology

Build this as a native Windows desktop application.

Preferred stack:

    C#
    .NET
    WPF

Use a currently supported stable .NET release suitable for Windows desktop development.

Prefer standard .NET/WPF functionality and keep external dependencies minimal.

The application should:

- Run completely offline.
- Require no cloud service.
- Require no Firebase.
- Require no web server.
- Require no internet connection.
- Store configuration and menu data locally.
- Recover its previous state after application restart.
- Be suitable for unreliable festival/event environments.

Internet connectivity must never be required for basic operation.

---

# 5. Application Architecture

The application should have two primary visual surfaces.

## A. Operator / Control Window

Displayed on the normal Windows monitor/laptop screen.

This is where the user manages:

- Menus
- Menu items
- Prices
- Categories
- Visibility
- Sold-out status
- Screen configuration
- Menu-to-screen assignments
- Output configuration
- Preview
- Calibration/testing

This window behaves like a normal Windows application.

## B. LED Output Window

Displayed on the monitor connected to the Mirackle VP2.

This window must:

- Be borderless.
- Have no title bar.
- Have no window chrome.
- Have no menu bar.
- Have no controls.
- Have no scrollbars.
- Fill the selected Windows display.
- Avoid displaying the Windows taskbar.
- Render configured Screens at their exact configured pixel coordinates.
- Use the same rendering system used by the operator preview.

The output window is essentially a rendering surface.

---

# 6. Multi-Monitor Behavior

The application must enumerate connected Windows displays.

The operator should be able to select which display is:

    Operator Display

and which display is:

    LED Output Display

The application must not assume monitor numbering remains identical forever.

Store enough information to make a reasonable attempt to restore the previously selected output monitor.

If the configured LED display cannot be found at startup:

- Do NOT blindly fullscreen onto the operator's primary monitor.
- Keep the output disabled.
- Clearly alert the operator.
- Allow another output display to be selected.

The operator must always retain control of the application.

---

# 7. Output Canvas

The LED output window should use the actual pixel dimensions of the selected Windows display.

Example:

    Output Width: 1920
    Output Height: 1080

Screens are positioned within that coordinate system.

For example:

    Screen 1:
        X = 0
        Y = 0
        Width = 336
        Height = 672

The remainder of the output canvas should default to pure black.

The application should prevent or warn about:

- Screens extending beyond the output canvas.
- Negative coordinates.
- Invalid dimensions.
- Accidental overlapping screens.

Overlapping screens may eventually be useful, so preferably warn rather than architecturally prohibit them unless there is a technical reason.

---

# 8. Screen Configuration

Create a Screen Configuration interface.

Each Screen should have at least:

    ID
    Name
    X
    Y
    Width
    Height
    Enabled
    Assigned Menu

Example:

    Name: Food Menu Wall
    X: 0
    Y: 0
    Width: 336
    Height: 672
    Enabled: Yes

Allow Screens to be:

- Added
- Edited
- Removed
- Enabled/disabled
- Assigned different Menus

The configuration UI should provide both:

## Numeric configuration

Editable fields for:

    X
    Y
    Width
    Height

## Visual configuration

Show a scaled representation of the entire output canvas.

Configured Screens should appear as rectangles inside it.

Eventually these rectangles should be draggable.

If practical, allow:

- Dragging to reposition.
- Resize handles.
- Snapping.
- Displaying X/Y/W/H while moving.

However, exact numeric input remains authoritative.

Do not sacrifice reliability for sophisticated drag/drop behavior.

---

# 9. Screen Configuration Presets

Design the data model so entire physical layouts can be saved as presets.

Examples:

    Single 2×2 Wall
    Dual 2×2 Walls
    Full 7×2 Wall

A preset describes screen positions/dimensions and output configuration.

Menu data should remain independent.

This does not need to become a complex preset-management system in the first milestone, but the architecture should not prevent it.

---

# 10. Menu Management

Menus should contain structured data rather than arbitrary positioned text boxes.

A Menu should include at minimum:

    ID
    Name
    Header / Display Name
    Optional subtitle
    Categories
    Menu Items
    Theme / template settings

A Menu Item should include at minimum:

    ID
    Name
    Description
    Price
    Category
    Visible
    Sold Out
    Featured
    Sort Order

Example:

    Name: Black & Blue Burger
    Description: Bacon • Blue Cheese • Onion
    Price: $14
    Visible: Yes
    Sold Out: No
    Featured: No

Do not assume prices must be numeric.

Price should support text because menus may contain values such as:

    $12
    $12 / $16
    Market Price
    3 for $10

---

# 11. Menu Editing

The operator must be able to change menu content quickly while an event is running.

Common actions must be extremely easy.

For each menu item provide quick controls for:

    Edit
    Sold Out
    Hide / Show

The operator should not have to open a complex editor simply to mark something sold out.

Changes should update the LED output immediately.

There should be no separate "publish" process for ordinary menu changes unless we later intentionally add one.

---

# 12. Sold Out vs Hidden

These are separate states.

## Sold Out

The item remains visible but is clearly marked unavailable.

The renderer/template determines the visual treatment.

Possible treatments include:

- Dimmed item
- Strike-through
- Large SOLD OUT indicator
- Overlay
- Replacement of price with SOLD OUT

Do not hard-code one treatment into the underlying menu data.

## Hidden

The item is completely removed from the rendered menu.

Remaining items should reflow appropriately.

---

# 13. Categories

Menus should support categories.

Examples:

    Burgers
    Appetizers
    Sides
    Cocktails
    Beer
    Specials

Categories should have:

    ID
    Name
    Visible
    Sort Order

Menu items belong to a category.

The rendering system should support category headers.

---

# 14. Templates and Layout

Do NOT create a free-form graphic design editor for the initial application.

The operator should edit structured menu data.

The renderer/template determines positioning.

This is intentional.

We want festival staff to be able to modify the menu without accidentally destroying the layout.

The rendering architecture should allow multiple templates eventually.

For the first template, optimize specifically for a:

    336×672 portrait LED display

Design principles:

- Large typography
- High contrast
- Generous spacing
- Strong visual hierarchy
- Minimal fine detail
- Good readability at distance
- Avoid thin lines and tiny text
- Avoid unnecessary graphical complexity

The system should be able to determine how much content fits.

Do not silently render text outside the screen bounds.

If menu content cannot fit, alert the operator.

Do not simply shrink all text until it becomes unreadable.

---

# 15. Preview

The operator window must contain a live preview.

CRITICAL REQUIREMENT:

The preview and LED output must use the SAME rendering component/system.

Do not create separate rendering implementations.

The preview should simply display a scaled representation of the same rendered result.

This prevents differences between:

    Preview

and

    Actual LED Output

Provide an optional 100% / pixel preview where practical.

The operator should be able to inspect the actual 336×672 rendered result.

---

# 16. Rendering and Pixel Accuracy

Pixel accuracy is important.

The menu renderer should conceptually render to the Screen's configured native dimensions.

For example:

    336×672

That rendered surface is then placed into the appropriate region of the larger output canvas.

Example:

    1920×1080 output

contains:

    Screen 1: 336×672 at X=0,Y=0

Do NOT design a 1920×1080 menu and scale it down to 336×672.

The goal is predictable rendering at the actual LED screen resolution.

Be aware of WPF's device-independent units and Windows DPI scaling.

The LED renderer must not accidentally interpret:

    1 WPF unit

as something other than:

    1 intended output pixel

because of DPI scaling.

Implement and test the output rendering so configured pixel coordinates correspond predictably to output pixels regardless of the DPI scaling used on the operator's monitor.

The operator should be allowed to keep normal Windows scaling such as 125% or 150%.

---

# 17. Calibration / Test Mode

Calibration tools are a first-class feature.

Provide an "Identify Screens" function.

When activated, each configured screen should display something similar to:

    SCREEN 1

    FOOD MENU

    336 × 672

Make the screen number very large.

For multiple screens, each should clearly show its own identity.

Also provide a calibration/test pattern.

At minimum include:

- Outer pixel boundary/border
- Corner markers
- Center marker/crosshair
- Horizontal center line
- Vertical center line
- Useful grid
- Screen number
- Native width × height

The purpose is to make VP2 configuration easy and reveal:

- Cropping
- Scaling
- Incorrect positioning
- Missing rows/columns
- Stretching
- Mapping errors

Do not use anti-aliased fuzzy lines where a crisp pixel boundary is required.

---

# 18. Blackout

Provide an immediately accessible:

    BLACKOUT

function.

When activated:

- All LED output becomes pure black.
- Operator interface remains operational.
- Menu configuration is not lost.
- Blackout can be toggled off to restore output.

Provide a keyboard shortcut.

Choose a shortcut unlikely to be pressed accidentally.

The operator interface must clearly indicate when blackout is active.

---

# 19. Local Persistence

Use straightforward local persistence.

JSON is preferred initially unless there is a strong technical reason otherwise.

Store:

- Application settings
- Display configuration
- Screen configurations
- Menus
- Categories
- Menu items
- Menu assignments
- Template/theme configuration

Use a sensible application-data directory rather than depending on the executable directory being writable.

Writes should be safe.

Avoid corrupting the entire configuration if the application or PC loses power while saving.

Consider:

1. Write temporary file.
2. Validate.
3. Atomically replace previous file.
4. Keep a backup of the last known-good configuration.

At festivals, abrupt shutdowns are realistic.

---

# 20. Assets

Support local image assets eventually, including:

- Logos
- Menu backgrounds
- Decorative images

Do not make remote URLs a requirement.

If assets are imported, consider copying them into an application-managed assets directory so moving/deleting the original file does not unexpectedly break a menu.

For the initial milestone, prioritize text menu reliability over sophisticated image management.

---

# 21. Autosave

Ordinary menu changes should save automatically.

Examples:

    Change price
    Mark Sold Out
    Hide item
    Change description

The operator should not have to remember to press Save during a busy event.

Avoid excessive disk writes from every keystroke.

Use an appropriate debounce strategy for text editing while making discrete actions such as Sold Out effectively immediate.

---

# 22. Failure Handling

The application is intended for live events.

Design for graceful failure.

Examples:

## Output monitor disconnected

Do not crash.

Notify the operator.

Attempt to restore output when appropriate, but do not unexpectedly hijack another display.

## Invalid configuration

Do not crash.

Explain the problem and prevent invalid rendering.

## Missing image

Do not crash.

Show a safe fallback and alert the operator.

## Corrupt configuration file

Attempt to recover from the last known-good backup.

Do not silently reset everything and destroy the user's menus.

## Renderer exception

Protect the operator/control application where reasonably possible.

A failure rendering one menu item should not necessarily destroy the entire application.

---

# 23. Logging

Implement local diagnostic logging.

Log important events such as:

- Application startup/shutdown
- Display enumeration
- Output display selected
- Output display lost
- Output window started/stopped
- Configuration loaded
- Configuration recovery
- Screen assignment changes
- Significant renderer failures

Do not flood the logs with every frame/render operation.

Logs should be useful for troubleshooting an installation at an event.

---

# 24. User Experience

Assume the application may sometimes be operated by someone who did not develop it.

The UI should prioritize:

    "Can a bartender/event employee understand this quickly?"

over:

    "Does this expose every possible technical setting?"

Advanced screen/output configuration can be separated from everyday menu operation.

The main operating screen should emphasize:

- Current menu
- Live output preview
- Menu items
- Sold Out
- Hide/Show
- Price editing
- Output status
- Blackout

Technical settings such as coordinates should not dominate normal operation.

---

# 25. Suggested Main Interface

A conceptual layout:

    ┌──────────────────────────────────────────────────┐
    │ LED MENU CONTROL                    OUTPUT: LIVE │
    ├──────────────────────────────┬───────────────────┤
    │                              │                   │
    │ FESTIVAL FOOD MENU           │ LIVE PREVIEW      │
    │                              │                   │
    │ BURGERS                      │ ┌───────────────┐ │
    │                              │ │               │ │
    │ Classic Burger      $12      │ │ BLACK & BLUE  │ │
    │ [Edit] [Sold Out] [Hide]     │ │               │ │
    │                              │ │ Burger    $12 │ │
    │ Black & Blue        $14      │ │ Fries      $6 │ │
    │ [Edit] [Sold Out] [Hide]     │ │               │ │
    │                              │ └───────────────┘ │
    │ SIDES                        │                   │
    │                              │ Screen: Food      │
    │ Fries                $6      │ 336×672           │
    │ [Edit] [Sold Out] [Hide]     │                   │
    │                              │                   │
    │ [+ Add Item]                 │                   │
    ├──────────────────────────────┴───────────────────┤
    │ Menus | Screens | Settings | Identify | BLACKOUT│
    └──────────────────────────────────────────────────┘

This is conceptual, not a pixel-perfect UI specification.

Improve it where appropriate while maintaining the intended workflow.

---

# 26. Keyboard Shortcuts

Provide useful keyboard shortcuts.

At minimum consider:

    Blackout toggle
    Identify Screens
    Exit fullscreen output / emergency recovery

Do not use shortcuts that make accidental destructive actions easy.

Document shortcuts somewhere accessible in the UI.

---

# 27. Output Window Recovery

It must always be possible to recover control of the output window.

Provide a keyboard shortcut from the operator application to:

    Stop Output

or otherwise close the LED fullscreen window without terminating the entire application.

Never create a fullscreen state from which the operator cannot easily recover.

---

# 28. Architecture Expectations

Use a clean architecture appropriate for a project of this size.

Separate concerns for:

- Models
- Persistence
- Menu management
- Screen/output management
- Rendering
- UI/view models
- Hardware/display discovery

Prefer MVVM patterns where they genuinely improve maintainability.

Do not overengineer the project with unnecessary enterprise abstractions.

However, the renderer should be cleanly separated because it is one of the most important pieces of the application.

There should be ONE authoritative menu rendering pipeline used by both preview and live output.

---

# 29. Testing

Testing is important.

At minimum provide automated tests for non-UI logic including:

- Screen coordinate validation
- Output bounds validation
- Screen overlap detection
- Menu ordering
- Category ordering
- Visibility behavior
- Sold-out state behavior
- Persistence serialization/deserialization
- Configuration validation
- Backup/recovery behavior where practical

Create renderer/layout tests where practical.

Test unusual cases:

- Empty menu
- One item
- Very long item name
- Very long description
- Long price string
- Many menu items
- Hidden category
- All items sold out
- All items hidden
- Output display missing
- Screen partially outside output
- Two overlapping screens
- Different DPI scaling between monitors

Do not claim a feature is complete merely because the application compiles.

---

# 30. Development Workflow

Do NOT attempt to implement the entire application in one giant pass.

First:

1. Read this entire specification.
2. Inspect the repository.
3. Determine the current development environment.
4. Produce an implementation plan.
5. Identify any genuine blocking questions.
6. Do not ask questions whose answers are already specified here.
7. Do not begin broad implementation until the architecture and milestones are clear.

Create and maintain:

    REQUIREMENTS.md
    DECISIONS.md
    IMPLEMENTATION_PLAN.md

## REQUIREMENTS.md

Convert this specification into individually trackable requirements.

Give requirements IDs, for example:

    DISP-001
    DISP-002
    MENU-001
    RENDER-001
    PERSIST-001

Each requirement should have a status such as:

    Not Started
    In Progress
    Implemented
    Verified

"Implemented" and "Verified" are different.

A requirement is only Verified when there is evidence that the behavior works.

## DECISIONS.md

Record meaningful architectural/product decisions.

Include:

    Decision
    Reason
    Date
    Relevant requirement IDs

Do not silently change requirements.

If implementation reveals that a requirement should change, STOP and ask for approval before changing the product behavior.

## IMPLEMENTATION_PLAN.md

Break implementation into small, reviewable phases.

Each phase should:

- Have explicit scope.
- Identify requirements being implemented.
- Define completion criteria.
- Include tests.
- Be independently reviewable.
- Preferably produce a working application state.

---

# 31. Suggested Milestones

You may refine these after inspecting the project, but a reasonable sequence is:

## Phase 1 — Foundation

- Create WPF project.
- Establish architecture.
- Local settings infrastructure.
- Logging.
- Basic operator shell.
- Automated test project.

## Phase 2 — Display Discovery

- Enumerate Windows monitors.
- Select output monitor.
- Detect resolution.
- Persist selection.
- Handle missing monitor safely.

## Phase 3 — Output Window

- Borderless fullscreen output.
- Correct monitor placement.
- Pure black unused canvas.
- Start/stop output.
- DPI/pixel-coordinate verification.

## Phase 4 — Screen Model

- Screen definitions.
- X/Y/W/H.
- Validation.
- Add/edit/remove.
- Visual output placement.

## Phase 5 — Calibration

- Identify Screens.
- Pixel border.
- Grid.
- Crosshair.
- Dimensions.
- Calibration rendering.

At this point we should be able to connect the PC to the VP2 and physically verify pixel mapping before investing heavily in menu design.

THIS IS AN IMPORTANT HUMAN VERIFICATION GATE.

Do not automatically proceed past this hardware verification milestone unless instructed.

## Phase 6 — Menu Data

- Menus.
- Categories.
- Items.
- Ordering.
- Visibility.
- Sold Out.
- Featured.
- Persistence.

## Phase 7 — Menu Renderer

- First 336×672 portrait template.
- Typography.
- Categories.
- Prices.
- Sold-out treatment.
- Overflow detection.

## Phase 8 — Operator Menu UI

- Menu selection.
- Quick price editing.
- Sold Out.
- Hide/Show.
- Add/edit items.
- Categories.

## Phase 9 — Live Preview

- Shared renderer.
- Scaled preview.
- Pixel/100% preview.

## Phase 10 — Blackout and Operational Controls

- Blackout.
- Keyboard shortcuts.
- Output status.
- Recovery controls.

## Phase 11 — Multiple Screens

- Multiple screen definitions.
- Independent menu assignments.
- Multiple rendered regions on one HDMI canvas.

## Phase 12 — Hardening

- Recovery behavior.
- Atomic persistence.
- Backup.
- Missing assets.
- Monitor disconnect.
- Logging review.
- Stress/edge testing.

## Phase 13 — Packaging

- Release build.
- Windows packaging/installer if appropriate.
- Clean-machine startup test.
- Operator documentation.

---

# 32. Human Verification Gates

Some requirements cannot be meaningfully verified by automated tests alone.

Explicitly stop and request human verification for:

## Hardware Pixel Mapping

After calibration/output support is implemented, ask the user to connect the PC to the Mirackle VP2.

Provide precise instructions for what should appear.

Ask the user to verify:

- Entire border visible.
- No cropping.
- No stretching.
- Center lines actually centered.
- Screen dimensions correct.
- VP2 mapping behaves as expected.

## Real LED Readability

After the first menu renderer is implemented, request physical testing on the LED panels.

Desktop preview is NOT sufficient evidence that typography is readable on the real LED wall.

Ask for feedback or photos before treating the initial template as final.

---

# 33. Scope Boundaries

Do NOT add these unless explicitly requested:

- Cloud accounts
- User authentication
- Remote administration
- Firebase
- Web hosting
- SaaS services
- Online synchronization
- Customer-facing ordering
- Payment processing
- Inventory management
- POS integration
- Arbitrary drag-and-drop menu design
- Video playback
- Animations simply for decoration
- AI-generated menu content

Keep version 1 focused on being an extremely reliable local LED menu controller.

---

# 34. Future-Proofing

Do not implement speculative features merely because they may be useful later.

However, avoid architecture that prevents likely future capabilities such as:

- Two or more independent LED screens.
- Different screen resolutions.
- Landscape screens.
- Saved physical screen configurations.
- Multiple menu templates.
- Logos/background assets.
- Import/export of menus.
- Menu duplication.
- Theme customization.
- Scheduled menu changes.
- Additional LED controllers.

These are future possibilities, not current requirements unless explicitly promoted later.

---

# 35. Definition of Done

The initial product should eventually allow this workflow:

1. Start Windows PC.
2. Launch LED Menu application.
3. Application restores menu/configuration.
4. Connect/select Mirackle VP2 output display.
5. Start LED output.
6. VP2 receives a standard Windows HDMI signal.
7. Application renders the configured 336×672 LED screen at the correct coordinates within that output.
8. Operator selects a Festival Food Menu.
9. Menu appears on LED wall.
10. Customer orders an item.
11. Item sells out.
12. Operator clicks Sold Out.
13. LED wall updates immediately.
14. Operator can later hide the item entirely.
15. Remaining content reflows.
16. Internet connectivity is never required.
17. Application remains controllable from the operator monitor.
18. A second independently configured menu/screen can later be added without redesigning the application's core architecture.

Reliability and readability are more important than visual complexity.

---

# 36. Instructions Before Coding

Before writing implementation code, respond with:

1. Your understanding of the system.
2. Proposed project architecture.
3. Proposed directory/project structure.
4. Proposed data model.
5. Proposed rendering strategy, specifically explaining how you will ensure predictable pixel rendering despite WPF DPI/device-independent units.
6. Proposed multi-monitor/output-window strategy.
7. Proposed local persistence strategy.
8. Proposed implementation phases.
9. Requirements or architectural concerns you think need clarification.
10. Any recommendations you believe would materially improve reliability.

Do not write production implementation code yet.

Do not silently make major product decisions.

Once this plan is approved, implement one phase at a time.

After each phase:

- Run the relevant automated tests.
- Build the complete solution.
- Report exactly what changed.
- Report tests executed and results.
- Report requirements moved to Implemented.
- Report requirements moved to Verified and the evidence.
- Identify anything requiring human verification.
- Update REQUIREMENTS.md, DECISIONS.md, and IMPLEMENTATION_PLAN.md.
- Create a focused git commit for the completed phase if the repository is under git.
- Do not proceed to the next human-verification gate without approval.