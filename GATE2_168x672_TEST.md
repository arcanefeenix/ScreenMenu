# Gate 2 extension — menu on a 168 × 672 screen (one panel wide, two high)

> **Status: PASSED (user approved the unchanged template as tested on 168×672).** See D-41. The adaptation proposed in section 5 was not adopted; only the mid-word fix was made.

The existing `portrait-basic` template has **not been redesigned** for this test. It renders natively at 168×672 using the same layout and renderer as 336×672: the type sizes are exactly the same (title 28, category bar 18, item name 20, price 20, description 14, etc.); the only thing that changes is the width the text has to wrap into (152 pixels instead of 320).

Gate 1 showed that a Screen at X=0, Y=0 maps to the top-left LED, and a panel is 168 × 336, so a **168 × 672 Screen at X=0, Y=0 is exactly the leftmost 1×2 panel column**.

## 1. Set up

1. **Screens** tab: select your Screen (or click **Add Screen (168×672)**).
2. Set it to **X=0, Y=0**. If it is the old 336×672 Screen, click **Set 168×672** on its card (or type Width 168, Height 672). Keep it enabled.
3. *(Optional but useful)* click **Add Screen (168×672)** again. The new Screen goes to **X=168, Y=0**, the second panel column. This will later be the video Screen; for now it lets you check that the two columns do not affect each other.
4. Start output (VP2 selected as in Gate 1).

## 2. First check the geometry: Screen Calibration

Choose **Screen Calibration**. Each 168×672 Screen should show:

- a complete one-LED white boundary on all four sides;
- four complete corner squares (red top-left, green top-right, blue bottom-left, yellow bottom-right);
- corner labels `0,0`, `167,0`, `0,671`, `167,671`, and a center label `84,336`;
- nothing from one Screen visible on the other, and nothing beyond the two columns.

Then switch back to **Normal Output**.

## 3. Menu samples (Menus tab → add the samples → pick one in the Screen's **Menu** drop-down)

At 168 pixels wide these menus need **more pages** than on 336 wide, because less fits across and names wrap more:

| Sample | Pages at 336×672 | Pages at 168×672 |
|---|---|---|
| Festival Food | 1 | **2** |
| Dense Menu | 3 | **4** |
| Sold Out Demo | 1 | **2** |
| Long Text Stress | 1 | **2** |

Pages rotate every 10 seconds as before.

## 3a. What I measured before you test (from the real renderer)

- Everything rendered. **No item was unrenderable**, and no menu reported a problem.
- No ordinary menu used a minimum type size. Only the deliberately extreme *Long Text Stress* menu (a 43-character title) dropped its title and subtitle to the minimum sizes (22 and 13).
- The layout engine itself has no hidden "336" assumption: it uses the screen's width. The only places 336 appeared were convenience defaults (the **Add Screen** size and the preview's size when no screen shows a menu), now supplemented by 168×672 buttons.
- The 168-wide screen holds roughly a third as much in one item as the 336-wide one (a single item can carry about 120 words of description versus 340 before it is reported as too big).
- Pagination, continued-category bars (`MAINS (CONT.)`), orphan prevention and hide/show recalculation all behave as at 336.

## 3b. Things I expect to look cramped (please judge them on the wall)

1. **Titles take two lines** (`FESTIVAL` / `FOOD`) on every page because 28 px type is wider than 152 px.
2. **Most item names wrap to two lines** (`Classic` / `Burger`), since the price takes room beside the name.
3. **Prices that do not fit beside the name drop to their own right-aligned line** (`3 for $10`, `Market Price`, `$6 / $9`).
4. **A real defect:** a long word beside an inline price can be cut in the middle: `Lemonade` shows as `Lemona` / `de` on Dense Menu page 3, and in Long Text Stress. I have not fixed this yet because the template was to be tested unchanged.
5. **Sold Out** badge usually sits on its own row under the struck-through name.
6. The repeated header costs roughly 15 percent of every page.
7. **Category bars** that continue show `(CONT.)`, and long category names wrap inside the bar.

## 4. Questions to answer from the wall

1. Is the type size still comfortable in a column this narrow, at your viewing distance? (It has not been shrunk.)
2. Do the wrapped names and right-aligned prices read clearly, or does it feel cramped or confusing?
3. Is the mid-word break (`Lemona`/`de`) as bad as it looks to me?
4. Is a two-line title too costly? Would a smaller or shorter title on later pages be better?
5. Do 2 to 4 pages at 10 seconds each feel right, or do you want fewer pages (see the proposal below)?
6. Anything clipped at the panel edges or at the boundary between your two columns?

## 5. Proposed narrow-screen adaptation (not built; for your approval after the test)

None of this shrinks the type.

1. **Never cut a word if it can be avoided.** If the longest word of an item name does not fit beside the price, put the price on its own row first. (This also applies at 336 wide.)
2. **Price on the description row** when the name wraps or the price is long: price right-aligned on the same line as the first description line when it fits, otherwise on its own line. This saves one row for many items.
3. **Compact continuation header** on pages 2 and later: the title only (one line if it fits at its smallest allowed size), no logo or subtitle. This frees roughly 100 pixels on each later page, about 15 percent. It changes the earlier decision that the whole header repeats on every page, so it needs your approval.
4. **Automatic:** a template variant (`portrait-narrow`) chosen when the screen is narrower than about 240 pixels, using the same fonts and sizes.

I would build and measure these only after you have seen the unchanged template on the wall.
