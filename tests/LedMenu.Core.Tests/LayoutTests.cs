using LedMenu.Core.Calibration;
using LedMenu.Core.Layout;
using LedMenu.Core.Menus;
using static LedMenu.Core.Layout.PortraitBasicTemplate;

namespace LedMenu.Core.Tests;

/// <summary>Every character is 0.5 em wide and a line is 1.25 em tall, so expected numbers can be worked out by hand.</summary>
internal sealed class FakeMeasurer : ITextMeasurer
{
    public double LineHeight(TextStyle style) => style.Size * 1.25;
    public double Width(string text, TextStyle style) => text.Length * style.Size * 0.5;
}

internal static class LayoutHelpers
{
    public const int W = 336, H = 672;
    public static readonly FakeMeasurer M = new();

    public static MenuLayoutResult Run(Menu menu, LogoMetrics? logo = null, int w = W, int h = H) =>
        MenuLayoutEngine.Layout(MenuView.Build(menu), menu.Theme, w, h, logo, M);

    public static IEnumerable<TextOp> Texts(MenuPage p) => p.Ops.OfType<TextOp>();
    public static IEnumerable<FillOp> Fills(MenuPage p) => p.Ops.OfType<FillOp>();
    public static bool HasText(MenuPage p, string text) => Texts(p).Any(t => t.Text == text);
    public static bool HasText(MenuLayoutResult r, string text) => r.Pages.Any(p => HasText(p, text));

    public static bool IsHeadingText(TextOp t) => t.Color == HeadingTextColor;
    // Roles are told apart by color: several roles share a font size and weight (price and name are both 20 bold).
    public static bool IsNameText(TextOp t) =>
        (t.Color == NameColor || t.Color == FeaturedNameColor || t.Color == SoldOutNameColor)
        && (t.Style == ItemName || t.Style == ItemNameMin);
    public static bool IsPriceText(TextOp t) => t.Color == PriceColor;
    public static bool IsDescriptionText(TextOp t) => t.Color == DescriptionColor || t.Color == SoldOutDescriptionColor;

    public static Menu OneCategory(int items, string category = "Burgers", string prefix = "Item", string price = "$1", string desc = "")
    {
        var m = new Menu { Name = "Test", HeaderText = "TEST" };
        var c = MenuEditor.AddCategory(m, category);
        for (var i = 1; i <= items; i++) MenuEditor.AddItem(m, c.Id, $"{prefix} {i}", price, desc);
        return m;
    }

    /// <summary>The y just below the lowest drawn thing on a page (excluding the page indicator).</summary>
    public static int ContentBottom(MenuPage p)
    {
        var bottom = 0;
        foreach (var op in p.Ops)
        {
            switch (op)
            {
                case FillOp f: bottom = Math.Max(bottom, f.Y + f.Height); break;
                case TextOp t when t.Style != PageIndicator: bottom = Math.Max(bottom, t.Y + (int)Math.Ceiling(M.LineHeight(t.Style))); break;
            }
        }
        return bottom;
    }
}

public class TextWrappingTests
{
    private static double W(string s) => s.Length * 10;   // 10 px per character

    [Fact]
    public void Short_text_stays_on_one_line() => Assert.Equal(new[] { "Hello world" }, TextWrapping.Wrap("Hello world", 200, W));

    [Fact]
    public void Breaks_at_spaces_when_the_line_is_full() =>
        Assert.Equal(new[] { "aaaa bbbb", "cccc" }, TextWrapping.Wrap("aaaa bbbb cccc", 90, W));

    [Fact]
    public void A_word_wider_than_the_line_is_split_by_character_never_dropped()
    {
        var lines = TextWrapping.Wrap("Supercalifragilistic", 80, W);
        Assert.True(lines.Count >= 3);
        Assert.Equal("Supercalifragilistic", string.Concat(lines));
        Assert.All(lines, l => Assert.True(W(l) <= 80));
    }

    [Fact]
    public void Explicit_line_breaks_are_kept()
    {
        Assert.Equal(new[] { "one", "two", "three" }, TextWrapping.Wrap("one\ntwo\r\nthree", 500, W));
        Assert.Equal(new[] { "a", "", "b" }, TextWrapping.Wrap("a\n\nb", 500, W));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_text_gives_one_empty_line(string? text) => Assert.Equal(new[] { "" }, TextWrapping.Wrap(text, 100, W));

    [Fact]
    public void Extra_spaces_collapse_and_no_line_is_too_wide()
    {
        var lines = TextWrapping.Wrap("  one   two three   four five  ", 100, W);
        Assert.All(lines, l => Assert.True(W(l) <= 100));
        Assert.Equal("one two three four five", string.Join(" ", lines));
    }

    [Fact]
    public void A_single_character_wider_than_the_line_is_still_emitted()
    {
        var lines = TextWrapping.Wrap("WWW", 5, W);
        Assert.Equal("WWW", string.Concat(lines));
    }
}

public class PageClockTests
{
    private static readonly Guid K = Guid.NewGuid();
    private static TimeSpan T(double s) => TimeSpan.FromSeconds(s);
    private static readonly TimeSpan P = TimeSpan.FromSeconds(10);

    [Fact]
    public void One_page_never_rotates()
    {
        var c = new PageClock();
        foreach (var t in new[] { 0, 5, 10, 999 }) Assert.Equal(0, c.PageFor(K, 1, P, T(t)));
        Assert.Equal(0, c.PageFor(K, 0, P, T(1000)));
    }

    [Fact]
    public void Pages_advance_every_period_and_wrap_back_to_the_first()
    {
        var c = new PageClock();
        Assert.Equal(0, c.PageFor(K, 3, P, T(100)));       // first sighting starts the clock
        Assert.Equal(0, c.PageFor(K, 3, P, T(109.9)));
        Assert.Equal(1, c.PageFor(K, 3, P, T(110)));
        Assert.Equal(2, c.PageFor(K, 3, P, T(120)));
        Assert.Equal(0, c.PageFor(K, 3, P, T(130)));       // back to page 1
        Assert.Equal(1, c.PageFor(K, 3, P, T(140)));
    }

    [Fact]
    public void Keys_run_on_independent_clocks()
    {
        var c = new PageClock();
        var other = Guid.NewGuid();
        c.PageFor(K, 2, P, T(0));
        c.PageFor(other, 2, P, T(5));
        Assert.Equal(1, c.PageFor(K, 2, P, T(10)));
        Assert.Equal(0, c.PageFor(other, 2, P, T(10)));
        Assert.Equal(1, c.PageFor(other, 2, P, T(15)));
    }

    [Fact]
    public void Changing_the_page_count_restarts_at_page_one()
    {
        var c = new PageClock();
        c.PageFor(K, 4, P, T(0));
        Assert.Equal(2, c.PageFor(K, 4, P, T(25)));
        Assert.Equal(0, c.PageFor(K, 3, P, T(26)));        // an item was hidden: new count, restart
        Assert.Equal(0, c.PageFor(K, 3, P, T(35)));
        Assert.Equal(1, c.PageFor(K, 3, P, T(36)));
    }

    [Fact]
    public void Unchanged_page_count_keeps_the_current_page_through_a_content_edit()
    {
        var c = new PageClock();
        c.PageFor(K, 3, P, T(0));
        Assert.Equal(1, c.PageFor(K, 3, P, T(12)));
        Assert.Equal(1, c.PageFor(K, 3, P, T(13)));        // same count: nothing restarts
    }

    [Fact]
    public void Reset_makes_the_next_sighting_start_a_fresh_period()
    {
        var c = new PageClock();
        c.PageFor(K, 2, P, T(0));
        Assert.Equal(1, c.PageFor(K, 2, P, T(12)));
        c.Reset(K, T(12));
        Assert.Equal(0, c.PageFor(K, 2, P, T(13)));
        c.ResetAll();
        Assert.Equal(0, c.PageFor(K, 2, P, T(500)));
    }

    [Fact]
    public void Period_is_clamped_so_a_bad_value_cannot_freeze_or_flicker_the_wall()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), PageClock.Period(10));
        Assert.Equal(TimeSpan.FromSeconds(PageClock.MinSeconds), PageClock.Period(0));
        Assert.Equal(TimeSpan.FromSeconds(PageClock.MinSeconds), PageClock.Period(-5));
        Assert.Equal(TimeSpan.FromSeconds(PageClock.MaxSeconds), PageClock.Period(100000));
        Assert.Equal(10, MenuTheme.DefaultPageSeconds);
    }

    [Fact]
    public void Time_going_backwards_does_not_crash_or_pick_a_negative_page()
    {
        var c = new PageClock();
        c.PageFor(K, 3, P, T(100));
        Assert.Equal(0, c.PageFor(K, 3, P, T(50)));
    }
}

public class TemplateTypographyTests
{
    [Fact]
    public void Every_role_has_an_explicit_minimum_no_larger_than_its_preferred_size()
    {
        Assert.True(HeaderMin.Size <= Header.Size);
        Assert.True(SubtitleMin.Size <= Subtitle.Size);
        Assert.True(CategoryMin.Size <= Category.Size);
        Assert.True(ItemNameMin.Size <= ItemName.Size);
        Assert.True(DescriptionMin.Size <= Description.Size);
        Assert.True(PriceMin.Size <= Price.Size);
        Assert.True(SoldOutMin.Size <= SoldOut.Size);
    }

    [Fact]
    public void Minimum_sizes_are_the_documented_floors()
    {
        Assert.Equal(22, HeaderMin.Size);
        Assert.Equal(16, CategoryMin.Size);
        Assert.Equal(18, ItemNameMin.Size);
        Assert.Equal(13, DescriptionMin.Size);
        Assert.Equal(18, PriceMin.Size);
        Assert.Equal(11, SoldOutMin.Size);
    }

    [Fact]
    public void Item_name_and_price_outrank_the_description()
    {
        Assert.True(ItemName.Size > Description.Size);
        Assert.True(Price.Size > Description.Size);
        Assert.Equal(FontWeightKind.Bold, ItemName.Weight);
        Assert.Equal(FontWeightKind.Bold, Price.Weight);
    }
}

public class LayoutBasicsTests
{
    [Fact]
    public void Empty_menu_gives_one_page_with_just_the_header()
    {
        var r = LayoutHelpers.Run(new Menu { Name = "Nothing", HeaderText = "EMPTY" });
        Assert.Equal(1, r.PageCount);
        Assert.False(r.HasOverflow);
        Assert.Contains(r.Pages[0].Ops, o => o is TextOp t && t.Text == "EMPTY");
        Assert.DoesNotContain(r.Pages[0].Ops, o => o is TextOp t && t.Style == ItemName);
        Assert.Equal((336, 672), (r.Width, r.Height));
    }

    [Fact]
    public void Header_only_with_title_subtitle_and_rule()
    {
        var m = new Menu { Name = "n", HeaderText = "FESTIVAL FOOD", Subtitle = "Fresh off the grill" };
        var r = LayoutHelpers.Run(m);
        var p = r.Pages[0];
        var title = LayoutHelpers.Texts(p).Single(t => t.Text == "FESTIVAL FOOD");
        var sub = LayoutHelpers.Texts(p).Single(t => t.Text == "Fresh off the grill");
        Assert.Equal(Header, title.Style);
        Assert.Equal(Subtitle, sub.Style);
        Assert.True(sub.Y > title.Y);
        var rule = LayoutHelpers.Fills(p).Single(f => f.Height == RuleHeight && f.Color == Accent);
        Assert.True(rule.Y > sub.Y);
        Assert.Equal((MarginX, 336 - 2 * MarginX), (rule.X, rule.Width));
    }

    [Fact]
    public void Title_is_centered_on_the_screen()
    {
        var r = LayoutHelpers.Run(new Menu { HeaderText = "MENU" });
        var t = LayoutHelpers.Texts(r.Pages[0]).Single();
        var w = (int)Math.Ceiling(LayoutHelpers.M.Width("MENU", Header));
        Assert.InRange(t.X + w / 2, 167, 169);
    }

    [Fact]
    public void Header_falls_back_to_the_menu_name_when_there_is_no_header_text()
    {
        var r = LayoutHelpers.Run(new Menu { Name = "Garlic Fest", HeaderText = "" });
        Assert.True(LayoutHelpers.HasText(r, "Garlic Fest"));
    }

    [Fact]
    public void A_title_too_long_for_two_lines_drops_to_the_minimum_header_size_and_still_fits()
    {
        var m = new Menu { HeaderText = "THE GREAT ANNUAL FESTIVAL OF FOOD AND DRINK" };
        var r = LayoutHelpers.Run(m);
        var titleTexts = LayoutHelpers.Texts(r.Pages[0]).Where(t => t.Style.Weight == FontWeightKind.Bold).ToList();
        Assert.All(titleTexts, t => Assert.Equal(HeaderMin, t.Style));
        Assert.Equal("THE GREAT ANNUAL FESTIVAL OF FOOD AND DRINK", string.Join(" ", titleTexts.Select(t => t.Text)));
    }

    [Fact]
    public void One_category_has_a_heading_band_then_its_items_in_order()
    {
        var r = LayoutHelpers.Run(LayoutHelpers.OneCategory(3));
        var p = r.Pages[0];
        var heading = LayoutHelpers.Texts(p).Single(LayoutHelpers.IsHeadingText);
        Assert.Equal("BURGERS", heading.Text);
        Assert.Equal(Category, heading.Style);
        var names = LayoutHelpers.Texts(p).Where(LayoutHelpers.IsNameText).ToList();
        Assert.Equal(new[] { "Item 1", "Item 2", "Item 3" }, names.Select(n => n.Text));
        Assert.True(names[0].Y > heading.Y);
        Assert.True(names[1].Y > names[0].Y && names[2].Y > names[1].Y);
        // the heading band is an accent-colored bar behind the heading text
        Assert.Contains(LayoutHelpers.Fills(p), f => f.Color == Accent && f.Y < heading.Y && f.Y + f.Height > heading.Y && f.Width == 320);
    }

    [Fact]
    public void Multiple_categories_appear_in_category_order_each_with_its_own_heading()
    {
        var m = SampleMenus.FestivalFood();
        var r = LayoutHelpers.Run(m);
        var headings = r.Pages.SelectMany(LayoutHelpers.Texts).Where(LayoutHelpers.IsHeadingText).Select(t => t.Text).ToList();
        Assert.Equal(new[] { "BURGERS", "APPETIZERS", "SIDES" }, headings);
    }

    [Fact]
    public void Price_is_right_aligned_on_the_name_line_and_ranks_with_the_name()
    {
        var r = LayoutHelpers.Run(LayoutHelpers.OneCategory(1, price: "$12"));
        var name = LayoutHelpers.Texts(r.Pages[0]).Single(LayoutHelpers.IsNameText);
        var price = LayoutHelpers.Texts(r.Pages[0]).Single(t => t.Text == "$12");
        Assert.Equal(name.Y, price.Y);
        Assert.Equal(Price, price.Style);
        Assert.Equal(PriceColor, price.Color);
        Assert.Equal(MarginX + 320, price.X + (int)Math.Ceiling(LayoutHelpers.M.Width("$12", Price)));
    }

    [Fact]
    public void Description_is_smaller_and_dimmer_than_the_name_and_sits_under_it()
    {
        var r = LayoutHelpers.Run(LayoutHelpers.OneCategory(1, desc: "Bacon and cheese"));
        var name = LayoutHelpers.Texts(r.Pages[0]).Single(LayoutHelpers.IsNameText);
        var desc = LayoutHelpers.Texts(r.Pages[0]).Single(t => t.Text == "Bacon and cheese");
        Assert.Equal(Description, desc.Style);
        Assert.Equal(DescriptionColor, desc.Color);
        Assert.True(desc.Y >= name.Y + (int)Math.Ceiling(LayoutHelpers.M.LineHeight(ItemName)));
    }

    [Fact]
    public void An_item_with_no_description_or_price_takes_only_its_name_line()
    {
        var m = new Menu { HeaderText = "T" };
        var c = MenuEditor.AddCategory(m, "C");
        MenuEditor.AddItem(m, c.Id, "A");
        MenuEditor.AddItem(m, c.Id, "B");
        var names = LayoutHelpers.Run(m).Pages[0].Ops.OfType<TextOp>().Where(LayoutHelpers.IsNameText).ToList();
        Assert.Equal(ItemGap + (int)Math.Ceiling(LayoutHelpers.M.LineHeight(ItemName)), names[1].Y - names[0].Y);
    }

    [Fact]
    public void Uncategorized_items_follow_the_categories_without_a_heading()
    {
        var m = LayoutHelpers.OneCategory(1);
        MenuEditor.AddItem(m, null, "Water", "Free");
        var r = LayoutHelpers.Run(m);
        var p = r.Pages[0];
        Assert.Single(LayoutHelpers.Texts(p), LayoutHelpers.IsHeadingText);
        var names = LayoutHelpers.Texts(p).Where(LayoutHelpers.IsNameText).ToList();
        Assert.Equal(new[] { "Item 1", "Water" }, names.Select(n => n.Text));
    }
}

public class LayoutLogoTests
{
    [Fact]
    public void No_logo_reserves_no_space_and_draws_no_logo()
    {
        var m = LayoutHelpers.OneCategory(1);
        var without = LayoutHelpers.Run(m);
        var with = LayoutHelpers.Run(m, new LogoMetrics(200, 100));
        Assert.DoesNotContain(without.Pages[0].Ops, o => o is LogoOp);
        Assert.Single(with.Pages[0].Ops.OfType<LogoOp>());

        var yWithout = LayoutHelpers.Texts(without.Pages[0]).First(LayoutHelpers.IsNameText).Y;
        var yWith = LayoutHelpers.Texts(with.Pages[0]).First(LayoutHelpers.IsNameText).Y;
        Assert.Equal(LogoMaxHeight + LogoGap, yWith - yWithout);   // exactly the logo's band is reclaimed when it is absent
    }

    [Theory]
    [InlineData(200, 100)]
    [InlineData(1000, 100)]
    [InlineData(50, 200)]
    [InlineData(64, 64)]
    [InlineData(3000, 1000)]
    [InlineData(10, 10)]
    public void Logo_keeps_its_proportions_and_stays_inside_its_box(int lw, int lh)
    {
        var r = LayoutHelpers.Run(LayoutHelpers.OneCategory(1), new LogoMetrics(lw, lh));
        var op = r.Pages[0].Ops.OfType<LogoOp>().Single();
        Assert.InRange(op.Width, 1, 320);
        Assert.InRange(op.Height, 1, LogoMaxHeight);
        Assert.True(op.X >= MarginX && op.X + op.Width <= MarginX + 320);
        // aspect preserved to within one pixel of rounding on the smaller side
        var expected = (double)lw / lh;
        var actual = (double)op.Width / op.Height;
        var tolerance = 1.0 / Math.Min(op.Width, op.Height) * Math.Max(1, expected) + 0.02;
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }

    [Fact]
    public void Logo_is_centered_and_repeats_on_every_page()
    {
        var m = LayoutHelpers.OneCategory(40, desc: "A line of description");
        var r = LayoutHelpers.Run(m, new LogoMetrics(200, 100));
        Assert.True(r.PageCount > 1);
        foreach (var p in r.Pages)
        {
            var op = Assert.Single(p.Ops.OfType<LogoOp>());
            Assert.Equal(MarginX + (320 - op.Width) / 2, op.X);
        }
    }

    [Fact]
    public void A_logo_with_zero_size_is_ignored_like_no_logo()
    {
        var r = LayoutHelpers.Run(LayoutHelpers.OneCategory(1), new LogoMetrics(0, 0));
        Assert.DoesNotContain(r.Pages[0].Ops, o => o is LogoOp);
    }
}

public class LayoutItemStateTests
{
    private static Menu Two(Action<MenuItem>? edit = null)
    {
        var m = LayoutHelpers.OneCategory(2, desc: "Description");
        edit?.Invoke(m.Items[0]);
        return m;
    }

    [Fact]
    public void Sold_out_keeps_the_items_place_and_replaces_the_price_with_a_badge()
    {
        var normal = LayoutHelpers.Run(Two());
        var sold = LayoutHelpers.Run(Two(i => i.SoldOut = true));
        var p = sold.Pages[0];

        // same names, same vertical positions: nothing moved
        var n0 = LayoutHelpers.Texts(normal.Pages[0]).Where(LayoutHelpers.IsNameText).Select(t => t.Y).ToList();
        var n1 = LayoutHelpers.Texts(p).Where(LayoutHelpers.IsNameText).Select(t => t.Y).ToList();
        Assert.Equal(n0, n1);
        Assert.Equal(normal.Pages[0].Ops.Count(o => o is TextOp t && LayoutHelpers.IsNameText(t)), p.Ops.Count(o => o is TextOp t && LayoutHelpers.IsNameText(t)));

        // badge: red box with SOLD OUT text; the price of that item is gone, the other item's price remains
        Assert.Contains(LayoutHelpers.Fills(p), f => f.Color == BadgeColor);
        Assert.Contains(LayoutHelpers.Texts(p), t => t.Text == SoldOutText && t.Color == BadgeTextColor && t.Style == SoldOut);
        Assert.Single(LayoutHelpers.Texts(p), t => t.Text == "$1");

        // dimmed name and description, struck through
        var name = LayoutHelpers.Texts(p).First(LayoutHelpers.IsNameText);
        Assert.Equal(SoldOutNameColor, name.Color);
        Assert.Contains(LayoutHelpers.Fills(p), f => f.Color == StrikeColor && f.Y > name.Y && f.Y < name.Y + 25);
        Assert.Equal(SoldOutDescriptionColor, LayoutHelpers.Texts(p).First(t => t.Text == "Description").Color);
    }

    [Fact]
    public void Several_sold_out_items_do_not_change_the_page_count_or_flood_the_page()
    {
        var m = LayoutHelpers.OneCategory(12, desc: "Description");
        var before = LayoutHelpers.Run(m);
        foreach (var i in m.Items.Where((_, idx) => idx % 2 == 0)) i.SoldOut = true;
        var after = LayoutHelpers.Run(m);
        Assert.Equal(before.PageCount, after.PageCount);
        // the badge is the only saturated color: exactly one per sold-out item
        Assert.Equal(6, after.Pages.Sum(p => LayoutHelpers.Fills(p).Count(f => f.Color == BadgeColor)));
    }

    [Fact]
    public void Sold_out_with_a_long_price_still_shows_the_badge_on_its_own_row()
    {
        var m = LayoutHelpers.OneCategory(1, price: "Market Price (ask at the window)");
        m.Items[0].SoldOut = true;
        var p = LayoutHelpers.Run(m).Pages[0];
        Assert.Contains(LayoutHelpers.Fills(p), f => f.Color == BadgeColor && f.X + f.Width == MarginX + 320);
        Assert.DoesNotContain(LayoutHelpers.Texts(p), t => t.Text.StartsWith("Market"));
    }

    [Fact]
    public void Featured_adds_emphasis_without_changing_any_height_or_position()
    {
        var plain = LayoutHelpers.Run(Two());
        var featured = LayoutHelpers.Run(Two(i => i.Featured = true));
        var a = LayoutHelpers.Texts(plain.Pages[0]).Select(t => (t.Text, t.X, t.Y)).ToList();
        var b = LayoutHelpers.Texts(featured.Pages[0]).Select(t => (t.Text, t.X, t.Y)).ToList();
        Assert.Equal(a, b);                                                            // not one text moved
        Assert.Equal(LayoutHelpers.ContentBottom(plain.Pages[0]), LayoutHelpers.ContentBottom(featured.Pages[0]));

        var name = LayoutHelpers.Texts(featured.Pages[0]).First(LayoutHelpers.IsNameText);
        Assert.Equal(FeaturedNameColor, name.Color);
        var bar = LayoutHelpers.Fills(featured.Pages[0]).Single(f => f.X == FeaturedBarX && f.Width == FeaturedBarWidth);
        Assert.Equal(Accent, bar.Color);
        Assert.Equal(name.Y, bar.Y);
        Assert.True(bar.X + bar.Width <= MarginX);                                     // inside the margin, not over any text
    }

    [Fact]
    public void Featured_does_not_change_pagination()
    {
        var m = LayoutHelpers.OneCategory(30, desc: "Description");
        var before = LayoutHelpers.Run(m);
        foreach (var i in m.Items.Where((_, idx) => idx % 3 == 0)) i.Featured = true;
        var after = LayoutHelpers.Run(m);
        Assert.Equal(before.PageCount, after.PageCount);
        for (var i = 0; i < before.PageCount; i++)
            Assert.Equal(LayoutHelpers.Texts(before.Pages[i]).Select(t => (t.Text, t.X, t.Y)),
                         LayoutHelpers.Texts(after.Pages[i]).Select(t => (t.Text, t.X, t.Y)));
    }

    [Fact]
    public void A_sold_out_featured_item_shows_as_sold_out_not_featured()
    {
        var p = LayoutHelpers.Run(Two(i => { i.Featured = true; i.SoldOut = true; })).Pages[0];
        Assert.DoesNotContain(LayoutHelpers.Fills(p), f => f.X == FeaturedBarX && f.Width == FeaturedBarWidth);
        Assert.Contains(LayoutHelpers.Fills(p), f => f.Color == BadgeColor);
    }
}

public class LayoutLongContentTests
{
    [Fact]
    public void A_long_item_name_wraps_and_the_item_grows_by_whole_lines()
    {
        var m = LayoutHelpers.OneCategory(1, price: "$9");
        m.Items[0].Name = "Smoked Brisket and Pulled Pork Combination Platter with Two Sides";
        var p = LayoutHelpers.Run(m).Pages[0];
        var lines = LayoutHelpers.Texts(p).Where(LayoutHelpers.IsNameText).ToList();
        Assert.True(lines.Count >= 3);
        Assert.Equal(m.Items[0].Name, string.Join(" ", lines.Select(l => l.Text)));
        var lh = (int)Math.Ceiling(LayoutHelpers.M.LineHeight(ItemName));
        for (var i = 1; i < lines.Count; i++) Assert.Equal(lh, lines[i].Y - lines[i - 1].Y);
        // every line stays left of the price column
        var priceX = LayoutHelpers.Texts(p).Single(t => t.Text == "$9").X;
        Assert.All(lines, l => Assert.True(l.X + LayoutHelpers.M.Width(l.Text, ItemName) <= priceX - NameGap + 0.5));
    }

    [Fact]
    public void A_long_description_wraps_within_the_content_width()
    {
        var m = LayoutHelpers.OneCategory(1, desc: string.Join(" ", Enumerable.Repeat("crispy golden battered", 8)));
        var p = LayoutHelpers.Run(m).Pages[0];
        var lines = LayoutHelpers.Texts(p).Where(LayoutHelpers.IsDescriptionText).ToList();
        Assert.True(lines.Count >= 4);
        Assert.All(lines, l => Assert.True(l.X + LayoutHelpers.M.Width(l.Text, Description) <= MarginX + 320));
    }

    [Fact]
    public void A_long_free_text_price_moves_to_its_own_right_aligned_row_below_the_name()
    {
        var m = LayoutHelpers.OneCategory(1, price: "Market Price (ask us)");
        var p = LayoutHelpers.Run(m).Pages[0];
        var name = LayoutHelpers.Texts(p).Single(LayoutHelpers.IsNameText);
        var price = LayoutHelpers.Texts(p).Single(t => LayoutHelpers.IsPriceText(t));
        Assert.True(price.Y >= name.Y + (int)Math.Ceiling(LayoutHelpers.M.LineHeight(ItemName)));
        Assert.Equal(MarginX + 320, price.X + (int)Math.Ceiling(LayoutHelpers.M.Width(price.Text, Price)));
    }

    [Fact]
    public void Short_prices_stay_on_the_name_line_for_every_kind_of_free_text()
    {
        foreach (var price in new[] { "$12", "$12 / $16", "3 for $10", "MP" })
        {
            var p = LayoutHelpers.Run(LayoutHelpers.OneCategory(1, price: price)).Pages[0];
            var name = LayoutHelpers.Texts(p).Single(LayoutHelpers.IsNameText);
            var pr = LayoutHelpers.Texts(p).Single(t => LayoutHelpers.IsPriceText(t));
            Assert.Equal(name.Y, pr.Y);
        }
    }

    [Fact]
    public void A_price_wider_than_the_whole_row_wraps_instead_of_running_off_the_screen()
    {
        var m = LayoutHelpers.OneCategory(1, price: string.Join(" ", Enumerable.Repeat("seasonal market pricing", 4)));
        var p = LayoutHelpers.Run(m).Pages[0];
        var prices = LayoutHelpers.Texts(p).Where(t => LayoutHelpers.IsPriceText(t)).ToList();
        Assert.True(prices.Count >= 2);
        Assert.All(prices, t => Assert.True(t.X >= MarginX && t.X + LayoutHelpers.M.Width(t.Text, Price) <= MarginX + 320 + 1));
    }

    [Fact]
    public void Text_with_unusual_characters_and_line_breaks_does_not_break_layout()
    {
        var m = LayoutHelpers.OneCategory(1);
        m.Items[0].Name = "Jalapeño — \"Hot\" & <Spicy>";
        m.Items[0].Description = "Line one\nLine two\r\nLine three";
        var r = LayoutHelpers.Run(m);
        Assert.False(r.HasOverflow);
        Assert.True(LayoutHelpers.HasText(r, "Line two"));
    }
}

public class PaginationTests
{
    private static Menu Many(int items, string desc = "Fresh and tasty") => LayoutHelpers.OneCategory(items, desc: desc);

    [Fact]
    public void A_menu_that_fits_is_one_page_with_no_page_indicator()
    {
        var r = LayoutHelpers.Run(Many(3));
        Assert.Equal(1, r.PageCount);
        Assert.DoesNotContain(r.Pages[0].Ops, o => o is TextOp t && t.Style == PageIndicator);
    }

    [Fact]
    public void The_boundary_between_one_and_two_pages_is_exact()
    {
        var n = 1;
        while (LayoutHelpers.Run(Many(n)).PageCount == 1) n++;
        var fitsOne = LayoutHelpers.Run(Many(n - 1));
        var needsTwo = LayoutHelpers.Run(Many(n));
        Assert.Equal(1, fitsOne.PageCount);
        Assert.Equal(2, needsTwo.PageCount);
        // the last item on the one-page menu really does end inside the page
        Assert.True(LayoutHelpers.ContentBottom(fitsOne.Pages[0]) <= 672 - MarginBottom);
        // and one more item would not have fitted
        var lh = (int)Math.Ceiling(LayoutHelpers.M.LineHeight(ItemName)) + 1 + (int)Math.Ceiling(LayoutHelpers.M.LineHeight(Description));
        Assert.True(LayoutHelpers.ContentBottom(fitsOne.Pages[0]) + ItemGap + lh > 672 - MarginBottom);
    }

    [Fact]
    public void A_menu_needing_two_pages_has_two_numbered_pages_with_indicators()
    {
        var r = LayoutHelpers.Run(Many(14));
        Assert.Equal(2, r.PageCount);
        Assert.Equal(new[] { 1, 2 }, r.Pages.Select(p => p.Number));
        Assert.All(r.Pages, p => Assert.Equal(2, p.Count));
        Assert.True(LayoutHelpers.HasText(r.Pages[0], "1/2"));
        Assert.True(LayoutHelpers.HasText(r.Pages[1], "2/2"));
    }

    [Fact]
    public void A_long_menu_takes_several_pages_and_every_item_appears_exactly_once_in_order()
    {
        var m = Many(45);
        var r = LayoutHelpers.Run(m);
        Assert.True(r.PageCount >= 4);
        var names = r.Pages.SelectMany(LayoutHelpers.Texts).Where(LayoutHelpers.IsNameText).Select(t => t.Text).ToList();
        Assert.Equal(m.Items.OrderBy(i => i.SortOrder).Select(i => i.Name), names);
        Assert.False(r.HasOverflow);
    }

    [Fact]
    public void Every_page_repeats_the_header_and_nothing_is_drawn_outside_the_page()
    {
        var m = Many(45);
        m.HeaderText = "FESTIVAL";
        var r = LayoutHelpers.Run(m);
        foreach (var p in r.Pages)
        {
            Assert.True(LayoutHelpers.HasText(p, "FESTIVAL"));
            foreach (var op in p.Ops)
            {
                switch (op)
                {
                    case FillOp f: Assert.True(f.X >= 0 && f.Y >= 0 && f.X + f.Width <= 336 && f.Y + f.Height <= 672, $"{f}"); break;
                    case TextOp t:
                        var w = LayoutHelpers.M.Width(t.Text, t.Style);
                        var h = LayoutHelpers.M.LineHeight(t.Style);
                        Assert.True(t.X >= 0 && t.Y >= 0 && t.X + w <= 336 && t.Y + h <= 672, $"{t}");
                        break;
                }
            }
        }
    }

    [Fact]
    public void Content_never_runs_into_the_page_indicator_strip()
    {
        var r = LayoutHelpers.Run(Many(45));
        var indicatorTop = 672 - MarginBottom - (int)Math.Ceiling(LayoutHelpers.M.LineHeight(PageIndicator));
        foreach (var p in r.Pages) Assert.True(LayoutHelpers.ContentBottom(p) <= indicatorTop);
    }

    [Fact]
    public void Turning_the_page_indicator_off_leaves_the_numbers_out_and_gives_the_space_back()
    {
        var m = Many(45);
        var withIndicator = LayoutHelpers.Run(m);
        m.Theme.ShowPageIndicator = false;
        var without = LayoutHelpers.Run(m);
        Assert.DoesNotContain(without.Pages.SelectMany(p => p.Ops), o => o is TextOp t && t.Style == PageIndicator);
        Assert.True(without.PageCount <= withIndicator.PageCount);
    }

    [Fact]
    public void An_item_is_never_split_across_pages()
    {
        var m = new Menu { HeaderText = "T" };
        var c = MenuEditor.AddCategory(m, "Cat");
        for (var i = 1; i <= 30; i++)
            MenuEditor.AddItem(m, c.Id, $"Dish {i}", "$5", $"MARK{i} one two three four five six seven eight nine ten eleven twelve");
        var r = LayoutHelpers.Run(m);
        Assert.True(r.PageCount > 2);
        for (var i = 1; i <= 30; i++)
        {
            var namePage = r.Pages.Single(p => LayoutHelpers.HasText(p, $"Dish {i}")).Number;
            var descPages = r.Pages.Where(p => LayoutHelpers.Texts(p).Any(t => t.Style == Description && t.Text.Contains($"MARK{i} ")))
                                   .Select(p => p.Number).ToList();
            Assert.Equal(new[] { namePage }, descPages);
            // all of the item's description lines are on its page: count them against a layout of that item alone
            var lines = TextWrapping.Wrap($"MARK{i} one two three four five six seven eight nine ten eleven twelve", 320,
                s => LayoutHelpers.M.Width(s, Description)).Count;
            var drawn = r.Pages[namePage - 1].Ops.OfType<TextOp>().Count(t => t.Style == Description);
            Assert.True(drawn >= lines);
        }
    }

    [Fact]
    public void A_category_that_crosses_a_page_boundary_repeats_its_heading_marked_continued()
    {
        var r = LayoutHelpers.Run(Many(45));
        var headings = r.Pages.Select(p => LayoutHelpers.Texts(p).Single(LayoutHelpers.IsHeadingText).Text).ToList();
        Assert.Equal("BURGERS", headings[0]);
        Assert.All(headings.Skip(1), h => Assert.Equal("BURGERS" + ContinuedSuffix, h));
    }

    [Fact]
    public void A_category_heading_is_never_left_alone_at_the_bottom_of_a_page()
    {
        // vary the number and size of items so the heading lands at many different heights
        for (var a = 1; a <= 14; a++)
            for (var b = 1; b <= 6; b++)
                for (var descWords = 0; descWords <= 12; descWords += 6)
                {
                    var m = new Menu { HeaderText = "T" };
                    var desc = string.Join(" ", Enumerable.Repeat("word", descWords));
                    var c1 = MenuEditor.AddCategory(m, "First");
                    var c2 = MenuEditor.AddCategory(m, "Second");
                    var c3 = MenuEditor.AddCategory(m, "Third");
                    for (var i = 0; i < a; i++) MenuEditor.AddItem(m, c1.Id, $"A{i}", "$1", desc);
                    for (var i = 0; i < b; i++) MenuEditor.AddItem(m, c2.Id, $"B{i}", "$2", desc);
                    for (var i = 0; i < 9 - b; i++) MenuEditor.AddItem(m, c3.Id, $"C{i}", "$3", desc);

                    var r = LayoutHelpers.Run(m);
                    foreach (var p in r.Pages)
                        foreach (var heading in LayoutHelpers.Texts(p).Where(LayoutHelpers.IsHeadingText))
                            Assert.Contains(LayoutHelpers.Texts(p), t => LayoutHelpers.IsNameText(t) && t.Y > heading.Y);
                }
    }

    [Fact]
    public void A_new_category_that_does_not_have_room_for_heading_plus_first_item_starts_the_next_page()
    {
        // fill page one until only a small gap is left, then add a category: its heading must move to page two with its item
        var m = new Menu { HeaderText = "T" };
        var c1 = MenuEditor.AddCategory(m, "First");
        var c2 = MenuEditor.AddCategory(m, "Second");
        var n = 1;
        MenuEditor.AddItem(m, c2.Id, "Only second item", "$1");
        while (true)
        {
            m.Items.RemoveAll(i => i.CategoryId == c1.Id);
            for (var i = 0; i < n; i++) MenuEditor.AddItem(m, c1.Id, $"F{i}", "$1", "d");
            var r = LayoutHelpers.Run(m);
            if (r.PageCount == 2 && LayoutHelpers.HasText(r.Pages[1], "SECOND") && !LayoutHelpers.HasText(r.Pages[0], "SECOND"))
            {
                Assert.True(LayoutHelpers.HasText(r.Pages[1], "Only second item"));
                // and the heading really is not stranded on page one
                Assert.False(LayoutHelpers.HasText(r.Pages[0], "Only second item"));
                return;
            }
            n++;
            Assert.True(n < 60, "never reached the boundary case");
        }
    }

    [Fact]
    public void Hidden_items_take_no_space_and_unhiding_restores_the_original_pages()
    {
        var m = Many(45);
        var original = LayoutHelpers.Run(m);

        // hide every other item: fewer pages, and no hidden name appears anywhere
        var hidden = m.Items.Where((_, idx) => idx % 2 == 1).ToList();
        foreach (var i in hidden) MenuEditor.SetVisible(m, i.Id, false);
        var reduced = LayoutHelpers.Run(m);
        Assert.True(reduced.PageCount < original.PageCount);
        foreach (var i in hidden) Assert.False(LayoutHelpers.HasText(reduced, i.Name));

        foreach (var i in hidden) MenuEditor.SetVisible(m, i.Id, true);
        var restored = LayoutHelpers.Run(m);
        Assert.Equal(original.PageCount, restored.PageCount);
        for (var p = 0; p < original.PageCount; p++)
            Assert.Equal(original.Pages[p].Ops, restored.Pages[p].Ops);
    }

    [Fact]
    public void Hiding_one_item_on_a_full_single_page_menu_never_adds_pages()
    {
        var n = 1;
        while (LayoutHelpers.Run(Many(n + 1)).PageCount == 1) n++;
        var m = Many(n);
        Assert.Equal(1, LayoutHelpers.Run(m).PageCount);
        MenuEditor.SetVisible(m, m.Items[0].Id, false);
        Assert.Equal(1, LayoutHelpers.Run(m).PageCount);
    }

    [Fact]
    public void A_hidden_category_takes_no_space_and_leaves_no_heading()
    {
        var m = SampleMenus.FestivalFood();
        var apps = m.Categories.First(c => c.Name == "Appetizers");
        MenuEditor.SetCategoryVisible(m, apps.Id, false);
        var r = LayoutHelpers.Run(m);
        var all = r.Pages.SelectMany(LayoutHelpers.Texts).ToList();
        Assert.DoesNotContain(all, t => t.Text == "APPETIZERS");
        Assert.DoesNotContain(all, t => t.Text == "Loaded Nachos");
    }

    [Fact]
    public void Everything_hidden_gives_the_same_page_as_an_empty_menu()
    {
        var m = SampleMenus.FestivalFood();
        foreach (var i in m.Items) i.Visible = false;
        var r = LayoutHelpers.Run(m);
        Assert.Equal(1, r.PageCount);
        Assert.DoesNotContain(r.Pages[0].Ops, o => o is TextOp t && LayoutHelpers.IsNameText(t));
    }

    [Fact]
    public void Every_sold_out_menu_is_still_fully_listed_with_the_same_pages()
    {
        var m = SampleMenus.FestivalFood();
        var before = LayoutHelpers.Run(m);
        foreach (var i in m.Items) i.SoldOut = true;
        var after = LayoutHelpers.Run(m);
        Assert.Equal(before.PageCount, after.PageCount);
        Assert.Equal(10, after.Pages.SelectMany(LayoutHelpers.Texts).Count(LayoutHelpers.IsNameText));
    }

    [Fact]
    public void Layout_is_deterministic()
    {
        var m = Many(45);
        var a = LayoutHelpers.Run(m);
        var b = LayoutHelpers.Run(m);
        Assert.Equal(a.PageCount, b.PageCount);
        for (var i = 0; i < a.PageCount; i++) Assert.Equal(a.Pages[i].Ops, b.Pages[i].Ops);
    }
}

public class OverflowTests
{
    private static string Words(int n) => string.Join(" ", Enumerable.Range(1, n).Select(i => "wordy" + i));

    [Fact]
    public void An_item_too_tall_for_any_page_is_reported_and_not_drawn_while_the_rest_still_render()
    {
        var m = LayoutHelpers.OneCategory(3);
        m.Items[1].Name = "Giant";
        m.Items[1].Description = Words(2000);
        var r = LayoutHelpers.Run(m);

        Assert.True(r.HasOverflow);
        var problem = Assert.Single(r.Problems);
        Assert.Equal(LayoutProblemKind.ItemTooTall, problem.Kind);
        Assert.Equal("Giant", problem.ItemName);
        Assert.Contains("not drawn", problem.Message);

        Assert.False(LayoutHelpers.HasText(r, "Giant"));                       // not clipped, not partially shown
        Assert.True(LayoutHelpers.HasText(r, "Item 1"));
        Assert.True(LayoutHelpers.HasText(r, "Item 3"));
    }

    [Fact]
    public void Overflow_is_cleared_when_the_item_is_shortened_or_hidden()
    {
        var m = LayoutHelpers.OneCategory(2);
        m.Items[0].Description = Words(2000);
        Assert.True(LayoutHelpers.Run(m).HasOverflow);
        MenuEditor.SetVisible(m, m.Items[0].Id, false);
        Assert.False(LayoutHelpers.Run(m).HasOverflow);
        MenuEditor.SetVisible(m, m.Items[0].Id, true);
        m.Items[0].Description = "Short";
        Assert.False(LayoutHelpers.Run(m).HasOverflow);
    }

    [Fact]
    public void An_item_that_only_fits_at_the_minimum_sizes_is_drawn_at_the_minimum_sizes()
    {
        // grow a description until it no longer fits at preferred sizes; look for a length that still fits at the minimum
        Menu? found = null;
        for (var n = 20; n < 400 && found == null; n++)
        {
            var m = LayoutHelpers.OneCategory(1);
            m.Items[0].Description = Words(n);
            var r = LayoutHelpers.Run(m);
            if (!r.HasOverflow && LayoutHelpers.Texts(r.Pages[0]).Any(t => t.Style == DescriptionMin)) found = m;
        }
        Assert.NotNull(found);
        var page = LayoutHelpers.Run(found!).Pages[0];
        Assert.All(LayoutHelpers.Texts(page).Where(t => t.Text.StartsWith("Item 1")), t => Assert.Equal(ItemNameMin, t.Style));
        Assert.All(LayoutHelpers.Texts(page).Where(t => t.Text.Contains("wordy")), t => Assert.Equal(DescriptionMin, t.Style));
    }

    [Fact]
    public void Ordinary_items_never_drop_to_the_minimum_sizes()
    {
        var r = LayoutHelpers.Run(LayoutHelpers.OneCategory(45, desc: "Fresh and tasty"));
        foreach (var t in r.Pages.SelectMany(LayoutHelpers.Texts))
        {
            if (LayoutHelpers.IsNameText(t)) Assert.Equal(ItemName, t.Style);
            else if (LayoutHelpers.IsPriceText(t)) Assert.Equal(Price, t.Style);
            else if (LayoutHelpers.IsDescriptionText(t)) Assert.Equal(Description, t.Style);
            else if (LayoutHelpers.IsHeadingText(t)) Assert.Equal(Category, t.Style);
        }
    }

    [Fact]
    public void Content_area_too_small_for_anything_reports_every_item_instead_of_clipping()
    {
        var m = LayoutHelpers.OneCategory(3);
        var r = LayoutHelpers.Run(m, w: 336, h: 100);
        Assert.True(r.HasOverflow);
        Assert.False(LayoutHelpers.HasText(r, "Item 1"));
    }
}

public class LayoutSizeTests
{
    [Theory]
    [InlineData(336, 672)]
    [InlineData(168, 336)]
    [InlineData(1176, 672)]
    public void Pages_are_exactly_the_requested_pixel_size_and_everything_stays_inside(int w, int h)
    {
        var r = LayoutHelpers.Run(SampleMenus.FestivalFood(), new LogoMetrics(200, 100), w, h);
        Assert.Equal((w, h), (r.Width, r.Height));
        foreach (var op in r.Pages.SelectMany(p => p.Ops))
        {
            switch (op)
            {
                case FillOp f: Assert.True(f.X >= 0 && f.Y >= 0 && f.X + f.Width <= w && f.Y + f.Height <= h); break;
                case LogoOp l: Assert.True(l.X >= 0 && l.Y >= 0 && l.X + l.Width <= w && l.Y + l.Height <= h); break;
                case TextOp t: Assert.True(t.X >= 0 && t.Y >= 0 && t.Y + LayoutHelpers.M.LineHeight(t.Style) <= h); break;
            }
        }
    }

    [Fact]
    public void The_shipped_sample_menu_lays_out_on_the_336_by_672_screen_without_problems()
    {
        var r = LayoutHelpers.Run(SampleMenus.FestivalFood());
        Assert.Equal((336, 672), (r.Width, r.Height));
        Assert.False(r.HasOverflow);
    }
}
