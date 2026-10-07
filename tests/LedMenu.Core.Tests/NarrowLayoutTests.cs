using LedMenu.Core.Layout;
using LedMenu.Core.Menus;
using static LedMenu.Core.Layout.PortraitBasicTemplate;

namespace LedMenu.Core.Tests;

/// <summary>The same template on a screen one panel wide: 168 x 672. Nothing is redesigned; the layout just gets less width.</summary>
public class NarrowLayoutTests
{
    private const int W = 168, H = 672, ContentW = W - 2 * MarginX;

    private static MenuLayoutResult Run(Menu m, LogoMetrics? logo = null) => LayoutHelpers.Run(m, logo, W, H);

    public static IEnumerable<object[]> Samples() => SampleMenus.All.Select(s => new object[] { s.Key });

    private static Menu Sample(string key) => SampleMenus.All.First(s => s.Key == key).Create();

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_sample_lays_out_at_168_by_672_with_every_item_drawn_once(string key)
    {
        var menu = Sample(key);
        var r = Run(menu);
        Assert.Equal((W, H), (r.Width, r.Height));
        Assert.False(r.HasOverflow, string.Join("; ", r.Problems.Select(p => p.Message)));
        Assert.True(r.PageCount >= 1);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void A_narrower_screen_never_needs_fewer_pages(string key)
    {
        var menu = Sample(key);
        Assert.True(Run(menu).PageCount >= LayoutHelpers.Run(menu).PageCount);
    }

    [Fact]
    public void Nothing_is_drawn_outside_the_168_pixel_page()
    {
        foreach (var info in SampleMenus.All)
            foreach (var op in Run(info.Create(), new LogoMetrics(300, 100)).Pages.SelectMany(p => p.Ops))
                switch (op)
                {
                    case FillOp f: Assert.True(f.X >= 0 && f.Y >= 0 && f.X + f.Width <= W && f.Y + f.Height <= H, $"{info.Key}: {f}"); break;
                    case LogoOp l: Assert.True(l.X >= MarginX && l.X + l.Width <= W - MarginX, $"{info.Key}: {l}"); break;
                    case TextOp t:
                        Assert.True(t.X >= 0 && t.Y >= 0 && t.Y + LayoutHelpers.M.LineHeight(t.Style) <= H, $"{info.Key}: {t}");
                        // a line may be at most as wide as the content area (right edge of aligned text is the right margin)
                        Assert.True(t.X + LayoutHelpers.M.Width(t.Text, t.Style) <= W - MarginX + 0.5, $"{info.Key}: {t.Text}");
                        break;
                }
    }

    [Fact]
    public void Typography_is_not_scaled_down_for_the_narrower_screen()
    {
        // every size that appears is one the template defines; no new, smaller "fitted" sizes are invented
        var defined = new HashSet<TextStyle>
        {
            Header, HeaderMin, Subtitle, SubtitleMin, Category, CategoryMin, ItemName, ItemNameMin,
            Description, DescriptionMin, Price, PriceMin, SoldOut, SoldOutMin, PageIndicator,
        };
        foreach (var info in SampleMenus.All)
            foreach (var t in Run(info.Create()).Pages.SelectMany(LayoutHelpers.Texts))
                Assert.Contains(t.Style, defined);

        var used = Run(SampleMenus.FestivalFood()).Pages.SelectMany(LayoutHelpers.Texts).Select(t => t.Style).ToHashSet();
        Assert.Contains(ItemName, used);
        Assert.Contains(Description, used);
        Assert.Contains(Category, used);
        Assert.Contains(Price, used);
    }

    [Fact]
    public void Ordinary_menus_stay_at_the_preferred_sizes_on_the_narrow_screen()
    {
        foreach (var key in new[] { "food", "dense", "soldout" })
            foreach (var t in Run(Sample(key)).Pages.SelectMany(LayoutHelpers.Texts))
            {
                if (LayoutHelpers.IsNameText(t)) Assert.Equal(ItemName, t.Style);
                else if (LayoutHelpers.IsPriceText(t)) Assert.Equal(Price, t.Style);
                else if (LayoutHelpers.IsDescriptionText(t)) Assert.Equal(Description, t.Style);
                else if (LayoutHelpers.IsHeadingText(t)) Assert.Equal(Category, t.Style);
            }
    }

    [Fact]
    public void The_logo_box_follows_the_screen_width_and_keeps_proportions()
    {
        var m = LayoutHelpers.OneCategory(2);
        var op = Run(m, new LogoMetrics(300, 100)).Pages[0].Ops.OfType<LogoOp>().Single();
        Assert.Equal((152, 51), (op.Width, op.Height));                // 3:1 fitted into 152 x 64
        Assert.Equal(MarginX + (ContentW - op.Width) / 2, op.X);

        var tall = Run(m, new LogoMetrics(40, 160)).Pages[0].Ops.OfType<LogoOp>().Single();
        Assert.Equal((16, 64), (tall.Width, tall.Height));
    }

    [Fact]
    public void Logo_and_header_repeat_on_every_page()
    {
        var r = Run(LayoutHelpers.OneCategory(40, desc: "A line"), new LogoMetrics(200, 100));
        Assert.True(r.PageCount > 2);
        Assert.All(r.Pages, p => Assert.Single(p.Ops.OfType<LogoOp>()));
        Assert.All(r.Pages, p => Assert.True(LayoutHelpers.HasText(p, "TEST") || LayoutHelpers.Texts(p).Any(t => t.Style == Header)));
    }

    [Fact]
    public void A_title_that_does_not_fit_one_line_wraps_to_two_at_the_full_size()
    {
        var m = new Menu { HeaderText = "FESTIVAL FOOD" };     // 13 chars: too wide for 152 px at 28 px type
        var titles = LayoutHelpers.Texts(Run(m).Pages[0]).ToList();
        Assert.Equal(2, titles.Count);
        Assert.All(titles, t => Assert.Equal(Header, t.Style));
    }

    [Fact]
    public void Category_headings_and_continued_markers_wrap_inside_the_bar()
    {
        var m = new Menu { HeaderText = "T" };
        var c = MenuEditor.AddCategory(m, "Specials and Limited Time Offers");
        for (var i = 0; i < 40; i++) MenuEditor.AddItem(m, c.Id, $"Item {i}", "$1", "desc");
        var r = Run(m);
        foreach (var page in r.Pages)
        {
            var bandTexts = LayoutHelpers.Texts(page).Where(LayoutHelpers.IsHeadingText).ToList();
            Assert.True(bandTexts.Count >= 2);                       // wrapped onto several lines
            foreach (var t in bandTexts)
                Assert.True(t.X + LayoutHelpers.M.Width(t.Text, t.Style) <= W - MarginX - HeadingPadX + 0.5);
        }
        Assert.EndsWith("OFFERS (CONT.)", string.Join(" ", LayoutHelpers.Texts(r.Pages[1]).Where(LayoutHelpers.IsHeadingText).Select(t => t.Text)));
    }

    [Fact]
    public void A_category_heading_is_never_left_alone_at_the_bottom_of_a_narrow_page()
    {
        for (var a = 1; a <= 14; a++)
            for (var b = 1; b <= 6; b++)
                for (var words = 0; words <= 8; words += 4)
                {
                    var m = new Menu { HeaderText = "T" };
                    var desc = string.Join(" ", Enumerable.Repeat("word", words));
                    var c1 = MenuEditor.AddCategory(m, "First");
                    var c2 = MenuEditor.AddCategory(m, "Second");
                    var c3 = MenuEditor.AddCategory(m, "Third");
                    for (var i = 0; i < a; i++) MenuEditor.AddItem(m, c1.Id, $"A{i}", "$1", desc);
                    for (var i = 0; i < b; i++) MenuEditor.AddItem(m, c2.Id, $"B{i}", "$2", desc);
                    for (var i = 0; i < 8 - b; i++) MenuEditor.AddItem(m, c3.Id, $"C{i}", "$3", desc);
                    var r = Run(m);
                    foreach (var p in r.Pages)
                        foreach (var h in LayoutHelpers.Texts(p).Where(LayoutHelpers.IsHeadingText))
                            Assert.Contains(LayoutHelpers.Texts(p), t => LayoutHelpers.IsNameText(t) && t.Y > h.Y);
                }
    }

    [Fact]
    public void Items_are_never_split_across_narrow_pages()
    {
        var m = new Menu { HeaderText = "T" };
        var c = MenuEditor.AddCategory(m, "Cat");
        for (var i = 1; i <= 30; i++)
            MenuEditor.AddItem(m, c.Id, $"Dish {i}", "$5", $"MARK{i} one two three four five six seven eight nine ten");
        var r = Run(m);
        Assert.True(r.PageCount > 3);
        for (var i = 1; i <= 30; i++)
        {
            var namePage = r.Pages.Single(p => LayoutHelpers.HasText(p, $"Dish {i}")).Number;
            var descPages = r.Pages.Where(p => LayoutHelpers.Texts(p).Any(t => t.Style == Description && t.Text.Contains($"MARK{i} "))).Select(p => p.Number);
            Assert.Equal(new[] { namePage }, descPages);
        }
    }

    [Fact]
    public void Hiding_and_showing_recalculates_the_narrow_pages_exactly()
    {
        var m = SampleMenus.DenseMenu();
        var original = Run(m);
        var hidden = m.Items.Where((_, i) => i % 3 == 0).ToList();
        foreach (var i in hidden) MenuEditor.SetVisible(m, i.Id, false);
        var reduced = Run(m);
        Assert.True(reduced.PageCount <= original.PageCount);
        foreach (var i in hidden) Assert.False(LayoutHelpers.HasText(reduced, i.Name));
        foreach (var i in hidden) MenuEditor.SetVisible(m, i.Id, true);
        var restored = Run(m);
        Assert.Equal(original.PageCount, restored.PageCount);
        for (var p = 0; p < original.PageCount; p++) Assert.Equal(original.Pages[p].Ops, restored.Pages[p].Ops);
    }

    [Fact]
    public void Sold_out_and_featured_do_not_move_anything_on_the_narrow_screen_unless_the_badge_needs_room()
    {
        var plain = Run(LayoutHelpers.OneCategory(6, desc: "Description"));
        var featuredMenu = LayoutHelpers.OneCategory(6, desc: "Description");
        foreach (var i in featuredMenu.Items) i.Featured = true;
        var featured = Run(featuredMenu);
        Assert.Equal(plain.PageCount, featured.PageCount);
        Assert.Equal(LayoutHelpers.Texts(plain.Pages[0]).Select(t => (t.Text, t.X, t.Y)),
                     LayoutHelpers.Texts(featured.Pages[0]).Select(t => (t.Text, t.X, t.Y)));

        var soldMenu = LayoutHelpers.OneCategory(6, desc: "Description");
        soldMenu.Items[2].SoldOut = true;
        var sold = Run(soldMenu);
        Assert.Contains(LayoutHelpers.Fills(sold.Pages[0]), f => f.Color == BadgeColor);
        Assert.Contains(LayoutHelpers.Fills(sold.Pages[0]), f => f.Color == StrikeColor);
        // the badge sits inside the page
        Assert.All(LayoutHelpers.Fills(sold.Pages[0]).Where(f => f.Color == BadgeColor), f => Assert.True(f.X + f.Width <= W - MarginX));
    }

    [Fact]
    public void A_name_that_is_one_long_word_is_still_drawn_in_full_by_splitting_it()
    {
        var m = LayoutHelpers.OneCategory(1);
        m.Items[0].Name = "Supercalifragilisticexpialidocious";
        var r = Run(m);
        Assert.False(r.HasOverflow);
        var drawn = string.Concat(LayoutHelpers.Texts(r.Pages[0]).Where(LayoutHelpers.IsNameText).Select(t => t.Text));
        Assert.Equal("Supercalifragilisticexpialidocious", drawn);
    }

    [Fact]
    public void An_item_too_big_for_a_narrow_page_is_reported_not_clipped_and_the_rest_still_draw()
    {
        var m = LayoutHelpers.OneCategory(3);
        m.Items[1].Name = "Giant";
        m.Items[1].Description = string.Join(" ", Enumerable.Range(1, 400).Select(i => "word" + i));
        var r = Run(m);
        Assert.Equal(LayoutProblemKind.ItemTooTall, Assert.Single(r.Problems).Kind);
        Assert.False(LayoutHelpers.HasText(r, "Giant"));
        Assert.True(LayoutHelpers.HasText(r, "Item 1"));
        Assert.True(LayoutHelpers.HasText(r, "Item 3"));
    }

    [Fact]
    public void The_narrow_screen_holds_less_than_the_wide_one_but_a_sensible_amount()
    {
        // how many words of description one item can carry on an empty page before it is reported as too big
        static int Capacity(int width)
        {
            var lo = 1;
            var hi = 4000;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                var m = LayoutHelpers.OneCategory(1);
                m.Items[0].Description = string.Join(" ", Enumerable.Repeat("word", mid));
                if (!LayoutHelpers.Run(m, null, width, H).HasOverflow) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
        var wide = Capacity(336);
        var narrow = Capacity(168);
        Assert.True(narrow < wide);
        Assert.True(narrow > 50, $"only {narrow} words fit");
    }
}
