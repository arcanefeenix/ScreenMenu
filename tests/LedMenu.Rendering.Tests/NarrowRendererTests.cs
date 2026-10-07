using System.Windows.Media;
using LedMenu.Core.Calibration;
using LedMenu.Core.Layout;
using LedMenu.Core.Menus;
using LedMenu.Rendering;
using Xunit.Abstractions;
using static LedMenu.Core.Layout.PortraitBasicTemplate;

namespace LedMenu.Rendering.Tests;

/// <summary>The existing template rendered natively on a 168 x 672 screen (one panel wide, two high).</summary>
public class NarrowRendererTests
{
    private const int W = 168, H = 672;
    private readonly ITestOutputHelper _out;

    public NarrowRendererTests(ITestOutputHelper output) => _out = output;

    private static MenuRenderResult Narrow(Menu m, string? logo = null) => R.Render(m, logo, W, H);

    [Fact]
    public void Every_sample_renders_natively_at_168_by_672_with_no_problems() => Sta.Run(() =>
    {
        foreach (var info in SampleMenus.All)
        {
            var r = Narrow(info.Create());
            Assert.Equal((W, H), (r.Width, r.Height));
            Assert.Empty(r.Problems);
            Assert.All(r.Pages, p => Assert.Equal((W, H), (p.Width, p.Height)));
        }
    });

    [Fact]
    public void Page_counts_on_the_narrow_screen_are_known_and_never_smaller_than_on_the_wide_one() => Sta.Run(() =>
    {
        var expected = new Dictionary<string, (int Wide, int Narrow)>
        {
            ["food"] = (1, 2), ["dense"] = (3, 4), ["soldout"] = (1, 2), ["stress"] = (1, 2),
        };
        foreach (var info in SampleMenus.All)
        {
            var wide = R.Render(info.Create()).PageCount;
            var narrow = Narrow(info.Create()).PageCount;
            _out.WriteLine($"{info.Title}: 336x672 = {wide} page(s), 168x672 = {narrow} page(s)");
            Assert.Equal(expected[info.Key], (wide, narrow));
            Assert.True(narrow >= wide);
        }
    });

    [Fact]
    public void Nothing_is_drawn_in_the_edge_pixel_columns_or_the_right_margin() => Sta.Run(() =>
    {
        foreach (var info in SampleMenus.All)
            foreach (var page in Narrow(info.Create()).Pages)
            {
                Assert.Equal(0, R.Count(page, R.NotBlack, 0, 0, 1, H));                 // x = 0
                Assert.Equal(0, R.Count(page, R.NotBlack, W - 5, 0, 5, H));             // x = 163..167
            }
    });

    [Fact]
    public void Typography_stays_at_the_preferred_sizes_and_is_the_same_height_as_on_the_wide_screen() => Sta.Run(() =>
    {
        // ordinary menus never touch a minimum size
        foreach (var key in new[] { "food", "dense", "soldout" })
            foreach (var t in Narrow(SampleMenus.All.First(s => s.Key == key).Create()).Layout.Pages.SelectMany(R.Texts))
            {
                if (t.Color == NameColor || t.Color == FeaturedNameColor || t.Color == SoldOutNameColor)
                { if (t.Style == ItemName || t.Style == ItemNameMin) Assert.Equal(ItemName, t.Style); }
                else if (t.Color == PriceColor) Assert.Equal(Price, t.Style);
                else if (t.Color == DescriptionColor || t.Color == SoldOutDescriptionColor) Assert.Equal(Description, t.Style);
                else if (t.Color == HeadingTextColor) Assert.Equal(Category, t.Style);
                else if (t.Color == TitleColor) Assert.Equal(Header, t.Style);
            }

        // rendered natively, not scaled: the ink of the first title line is the same height at both widths
        static int FirstTitleInkHeight(MenuRenderResult r)
        {
            var title = r.Layout.Pages[0].Ops.OfType<TextOp>().First(t => t.Color == TitleColor);
            var b = R.Bounds(r.Pages[0], c => c.R > 128 && c.G > 128 && c.B > 128, 0, title.Y, r.Width, 36)!.Value;
            return b.Y1 - b.Y0 + 1;
        }
        var wide = FirstTitleInkHeight(R.Render(SampleMenus.FestivalFood()));
        var narrow = FirstTitleInkHeight(Narrow(SampleMenus.FestivalFood()));
        Assert.InRange(narrow, wide - 1, wide + 1);
    });

    [Fact]
    public void Only_the_worst_case_text_uses_minimum_sizes() => Sta.Run(() =>
    {
        var stress = Narrow(SampleMenus.LongTextStress());
        var texts = stress.Layout.Pages.SelectMany(R.Texts).ToList();
        Assert.Contains(texts, t => t.Style == HeaderMin);                       // the very long title
        Assert.DoesNotContain(texts, t => t.Color == PriceColor && t.Style == PriceMin);
        Assert.DoesNotContain(texts, t => (t.Color == NameColor || t.Color == SoldOutNameColor) && t.Style == ItemNameMin);
    });

    [Fact]
    public void Category_headings_are_never_orphaned_with_the_real_font() => Sta.Run(() =>
    {
        for (var a = 1; a <= 10; a++)
            for (var b = 1; b <= 4; b++)
            {
                var m = new Menu { HeaderText = "T" };
                var c1 = MenuEditor.AddCategory(m, "First");
                var c2 = MenuEditor.AddCategory(m, "Second Category");
                for (var i = 0; i < a; i++) MenuEditor.AddItem(m, c1.Id, $"Item A{i}", "$1", "Short description");
                for (var i = 0; i < b + 5; i++) MenuEditor.AddItem(m, c2.Id, $"Item B{i}", "$2", i % 2 == 0 ? "A somewhat longer description line" : "");
                foreach (var page in Narrow(m).Layout.Pages)
                    foreach (var h in R.Texts(page).Where(t => t.Color == HeadingTextColor))
                        Assert.Contains(R.Texts(page), t => (t.Color == NameColor) && t.Style == ItemName && t.Y > h.Y);
            }
    });

    [Fact]
    public void A_continued_category_is_marked_and_the_heading_wraps_inside_its_bar() => Sta.Run(() =>
    {
        var r = Narrow(SampleMenus.DenseMenu());
        var headings = r.Layout.Pages.SelectMany(p => R.Texts(p).Where(t => t.Color == HeadingTextColor)).Select(t => t.Text).ToList();
        Assert.Contains(headings, h => h.Contains("(CONT.)"));
        Assert.Equal(0, R.Count(r.Pages[1], R.NotBlack, W - 5, 0, 5, H));
    });

    [Fact]
    public void Hiding_leaves_exactly_the_page_of_a_menu_without_the_item_and_showing_restores_it() => Sta.Run(() =>
    {
        var hidden = SampleMenus.FestivalFood();
        var removed = SampleMenus.FestivalFood();
        var victim = "Soft Pretzel";
        MenuEditor.SetVisible(hidden, hidden.Items.First(i => i.Name == victim).Id, false);
        removed.Items.Remove(removed.Items.First(i => i.Name == victim));
        var a = Narrow(hidden);
        var b = Narrow(removed);
        Assert.Equal(b.PageCount, a.PageCount);
        for (var p = 0; p < a.PageCount; p++) Assert.True(R.SamePixels(a.Pages[p], b.Pages[p]));

        var original = Narrow(SampleMenus.FestivalFood());
        MenuEditor.SetVisible(hidden, hidden.Items.First(i => i.Name == victim).Id, true);
        var restored = Narrow(hidden);
        for (var p = 0; p < original.PageCount; p++) Assert.True(R.SamePixels(original.Pages[p], restored.Pages[p]));
    });

    [Fact]
    public void Sold_out_and_featured_render_inside_the_narrow_page() => Sta.Run(() =>
    {
        var r = Narrow(SampleMenus.SoldOutDemo());
        Assert.Empty(r.Problems);
        foreach (var page in r.Layout.Pages)
        {
            foreach (var badge in page.Ops.OfType<FillOp>().Where(f => f.Color == BadgeColor))
                Assert.True(badge.X + badge.Width <= W - MarginX);
            foreach (var bar in page.Ops.OfType<FillOp>().Where(f => f.X == FeaturedBarX && f.Width == FeaturedBarWidth))
                Assert.True(bar.X + bar.Width <= MarginX);
        }
        // the badge is really drawn in red, and the strike-through in gray
        Assert.True(r.Pages.Sum(p => R.Count(p, c => R.Near(c, BadgeColor, 6))) > 500);
        Assert.True(r.Pages.Sum(p => R.Count(p, c => R.Near(c, StrikeColor, 6))) > 60);
    });

    [Fact]
    public void A_logo_fits_the_narrow_width_without_stretching() => Sta.Run(() =>
    {
        var magenta = Color.FromRgb(255, 0, 255);
        var path = R.MakeLogo(300, 100, magenta);
        var m = SampleMenus.FestivalFood();
        m.LogoAsset = "logo.png";
        var r = Narrow(m, path);
        var op = r.Layout.Pages[0].Ops.OfType<LogoOp>().Single();
        Assert.Equal((152, 51), (op.Width, op.Height));
        var b = R.Bounds(r.Pages[0], c => c.R > 250 && c.G < 6 && c.B > 250)!.Value;
        Assert.InRange((double)(b.X1 - b.X0 + 1) / (b.Y1 - b.Y0 + 1), 2.85, 3.15);
        Assert.True(b.X0 >= MarginX && b.X1 < W - MarginX);
        Assert.All(r.Pages, p => Assert.True(R.Count(p, c => c.R > 250 && c.G < 6 && c.B > 250) > 2000));   // repeated on every page
    });

    [Fact]
    public void A_missing_logo_on_the_narrow_screen_still_reclaims_its_space() => Sta.Run(() =>
    {
        var m = SampleMenus.FestivalFood();
        m.LogoAsset = "gone.png";
        var r = Narrow(m, null);
        Assert.Equal(RenderProblemKind.LogoMissing, Assert.Single(r.Problems).Kind);
        var reference = Narrow(SampleMenus.FestivalFood());
        Assert.Equal(reference.PageCount, r.PageCount);
        for (var p = 0; p < r.PageCount; p++) Assert.True(R.SamePixels(r.Pages[p], reference.Pages[p]));
    });

    [Fact]
    public void Two_168_wide_screens_side_by_side_do_not_touch_each_other() => Sta.Run(() =>
    {
        var left = Narrow(SampleMenus.FestivalFood()).Pages[0];
        var right = Narrow(SampleMenus.DenseMenu()).Pages[0];
        var frame = FrameComposer.ComposeScreens(1920, 1080, new[] { (0, 0, left), (168, 0, right) });
        for (var y = 0; y < H; y += 3)
            for (var x = 0; x < W; x++)
            {
                Assert.Equal(left.GetPixel(x, y), frame.GetPixel(x, y));
                Assert.Equal(right.GetPixel(x, y), frame.GetPixel(168 + x, y));
            }
        // beyond the second screen and below both: pure black
        Assert.Equal(0, R.Count(frame, R.NotBlack, 336, 0, 1584, 1080));
        Assert.Equal(0, R.Count(frame, R.NotBlack, 0, 672, 336, 408));
    });

    [Fact]
    public void Rendering_at_168_is_deterministic_and_independent_of_rendering_at_336() => Sta.Run(() =>
    {
        var m = SampleMenus.DenseMenu();
        var first = Narrow(m);
        _ = R.Render(m);                 // a render at another size in between must not change anything
        var second = Narrow(m);
        Assert.Equal(first.PageCount, second.PageCount);
        for (var p = 0; p < first.PageCount; p++) Assert.True(R.SamePixels(first.Pages[p], second.Pages[p]));
    });

    [Fact]
    public void An_item_too_big_for_a_narrow_page_is_reported_and_not_drawn() => Sta.Run(() =>
    {
        var m = R.Items(3);
        m.Items[1].Name = "Giant Platter";
        m.Items[1].Description = string.Join(" ", Enumerable.Repeat("an extremely long description that never ends", 90));
        var r = Narrow(m);
        Assert.Equal(RenderProblemKind.ItemOverflow, Assert.Single(r.Problems).Kind);
        Assert.DoesNotContain(r.Layout.Pages.SelectMany(R.Texts), t => t.Text.Contains("Giant"));
        Assert.Contains(r.Layout.Pages.SelectMany(R.Texts), t => t.Text == "Item 1");
    });

    [Fact]
    public void How_much_one_item_can_hold_on_the_wide_and_narrow_screens() => Sta.Run(() =>
    {
        static int Capacity(int width)
        {
            int lo = 1, hi = 3000;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                var m = R.Items(1, desc: string.Join(" ", Enumerable.Repeat("word", mid)));
                if (R.Render(m, null, width, 672).Problems.Count == 0) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
        var wide = Capacity(336);
        var narrow = Capacity(168);
        _out.WriteLine($"Largest description that still fits one empty page: 336 wide = {wide} words, 168 wide = {narrow} words");
        Assert.True(narrow < wide);
        Assert.True(narrow > 80);
    });

    // Found by the first 168-wide test and fixed afterwards: a long word beside an inline price used to be cut in the middle
    // ("Lemona" / "de"); now the price moves to its own row first.
    [Fact]
    public void Words_in_item_names_are_never_cut_in_the_middle_when_the_price_could_move() => Sta.Run(() =>
    {
        var r = Narrow(SampleMenus.DenseMenu());
        var names = r.Layout.Pages.SelectMany(R.Texts).Where(t => t.Color == NameColor && t.Style == ItemName).Select(t => t.Text).ToList();
        Assert.Contains("Lemonade", names);
        Assert.DoesNotContain("Lemona", names);
        Assert.DoesNotContain("de", names);

        // every sample: any word that fits the full line is drawn whole
        foreach (var info in SampleMenus.All)
        {
            var menu = info.Create();
            var drawn = Narrow(menu).Layout.Pages.SelectMany(R.Texts)
                .Where(t => (t.Color == NameColor || t.Color == FeaturedNameColor || t.Color == SoldOutNameColor) && (t.Style == ItemName)).Select(t => t.Text).ToList();
            var measurer = new WpfTextMeasurer(R.Fonts.Resolve(null).Family);
            foreach (var item in menu.Items)
                foreach (var word in item.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (measurer.Width(word, ItemName) <= 152)
                        Assert.Contains(drawn, line => line.Split(' ').Contains(word));
        }
    });
}
