using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedMenu.Core.Calibration;
using LedMenu.Core.Layout;
using LedMenu.Core.Menus;
using LedMenu.Rendering;
using static LedMenu.Core.Layout.PortraitBasicTemplate;

namespace LedMenu.Rendering.Tests;

internal static class R
{
    public static readonly FontCatalog Fonts = new(Path.Combine(AppContext.BaseDirectory, "Fonts"));

    public static MenuRenderResult Render(Menu m, string? logoPath = null, int w = 336, int h = 672) =>
        MenuRenderer.Render(m, w, h, Fonts, logoPath);

    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "ledmenu-render-tests");

    /// <summary>Writes a solid-color PNG and returns its path.</summary>
    public static string MakeLogo(int w, int h, Color color, string? name = null)
    {
        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, name ?? $"logo-{w}x{h}-{color.R}-{color.G}-{color.B}.png");
        var pixels = new byte[w * h * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255; }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }

    public static string MakeBrokenLogo()
    {
        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, "broken-logo.png");
        var bytes = new byte[400];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);   // looks like a PNG, is not one
        for (var i = 8; i < bytes.Length; i++) bytes[i] = (byte)(i * 7);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static bool Near(Rgb a, Rgb b, int tolerance = 24) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    /// <summary>Bounding box of all pixels satisfying the predicate inside a rectangle, or null if there are none.</summary>
    public static (int X0, int Y0, int X1, int Y1, int Count)? Bounds(PixelBuffer b, Func<Rgb, bool> pred, int rx = 0, int ry = 0, int? rw = null, int? rh = null)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1, n = 0;
        for (var y = Math.Max(0, ry); y < Math.Min(b.Height, ry + (rh ?? b.Height)); y++)
            for (var x = Math.Max(0, rx); x < Math.Min(b.Width, rx + (rw ?? b.Width)); x++)
                if (pred(b.GetPixel(x, y)))
                {
                    n++;
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                }
        return n == 0 ? null : (x0, y0, x1, y1, n);
    }

    public static int Count(PixelBuffer b, Func<Rgb, bool> pred, int rx = 0, int ry = 0, int? rw = null, int? rh = null) =>
        Bounds(b, pred, rx, ry, rw, rh)?.Count ?? 0;

    public static bool IsBlack(Rgb c) => c == Rgb.Black;
    public static bool NotBlack(Rgb c) => c != Rgb.Black;

    public static Menu Items(int count, string category = "Burgers", string desc = "Fresh and tasty", string price = "$1")
    {
        var m = new Menu { Name = "T", HeaderText = "TEST MENU" };
        var c = MenuEditor.AddCategory(m, category);
        for (var i = 1; i <= count; i++) MenuEditor.AddItem(m, c.Id, $"Item {i}", price, desc);
        return m;
    }

    public static IEnumerable<TextOp> Texts(MenuPage p) => p.Ops.OfType<TextOp>();

    public static bool SamePixels(PixelBuffer a, PixelBuffer b) => a.Width == b.Width && a.Height == b.Height && a.Data.AsSpan().SequenceEqual(b.Data);
}

public class RendererDimensionTests
{
    [Fact]
    public void Every_page_of_every_sample_is_exactly_336_by_672() => Sta.Run(() =>
    {
        foreach (var info in SampleMenus.All)
        {
            var r = R.Render(info.Create());
            Assert.Equal((336, 672), (r.Width, r.Height));
            Assert.NotEmpty(r.Pages);
            Assert.All(r.Pages, p => Assert.Equal((336, 672), (p.Width, p.Height)));
        }
    });

    [Theory]
    [InlineData(168, 336)]
    [InlineData(336, 672)]
    [InlineData(1176, 672)]
    [InlineData(200, 300)]
    public void Pages_are_the_screens_own_size_whatever_it_is(int w, int h) => Sta.Run(() =>
    {
        var r = R.Render(SampleMenus.FestivalFood(), null, w, h);
        Assert.All(r.Pages, p => Assert.Equal((w, h), (p.Width, p.Height)));
    });

    [Fact]
    public void Nothing_is_drawn_in_the_left_edge_or_far_right_margin() => Sta.Run(() =>
    {
        foreach (var info in SampleMenus.All)
            foreach (var page in R.Render(info.Create()).Pages)
            {
                Assert.Equal(0, R.Count(page, R.NotBlack, 0, 0, 1, 672));          // x = 0
                Assert.Equal(0, R.Count(page, R.NotBlack, 331, 0, 5, 672));        // x = 331..335
            }
    });

    [Fact]
    public void Rendering_is_deterministic() => Sta.Run(() =>
    {
        var m = SampleMenus.DenseMenu();
        var a = R.Render(m);
        var b = R.Render(m);
        Assert.Equal(a.PageCount, b.PageCount);
        for (var i = 0; i < a.PageCount; i++) Assert.True(R.SamePixels(a.Pages[i], b.Pages[i]));
    });

    [Fact]
    public void The_whole_page_is_opaque() => Sta.Run(() =>
    {
        var page = R.Render(SampleMenus.FestivalFood()).Pages[0];
        for (var i = 3; i < page.Data.Length; i += 4) Assert.Equal(255, page.Data[i]);
    });
}

public class RendererContentTests
{
    [Fact]
    public void Empty_menu_draws_only_the_header_and_rule() => Sta.Run(() =>
    {
        var r = R.Render(new Menu { Name = "Nothing", HeaderText = "NOTHING YET" });
        Assert.Equal(1, r.PageCount);
        Assert.Empty(r.Problems);
        var page = r.Pages[0];
        var rule = r.Layout.Pages[0].Ops.OfType<FillOp>().Single(f => f.Color == Accent && f.Height == RuleHeight);
        Assert.Equal(0, R.Count(page, R.NotBlack, 0, rule.Y + RuleHeight + 1, 336, 672 - rule.Y - RuleHeight - 1));   // nothing below the rule
        Assert.True(R.Count(page, c => R.Near(c, TitleColor), 0, 0, 336, rule.Y) > 100);                              // the title is there
        Assert.Equal(Accent, page.GetPixel(rule.X, rule.Y));
        Assert.Equal(Accent, page.GetPixel(rule.X + rule.Width - 1, rule.Y + RuleHeight - 1));
    });

    [Fact]
    public void Header_with_subtitle_draws_both_in_their_own_colors() => Sta.Run(() =>
    {
        var m = new Menu { Name = "x", HeaderText = "FESTIVAL FOOD", Subtitle = "Fresh off the grill" };
        var r = R.Render(m);
        var title = R.Texts(r.Layout.Pages[0]).Single(t => t.Text == "FESTIVAL FOOD");
        var sub = R.Texts(r.Layout.Pages[0]).Single(t => t.Text == "Fresh off the grill");
        var page = r.Pages[0];
        Assert.True(R.Count(page, c => R.Near(c, TitleColor, 10), 0, title.Y, 336, 36) > 100);
        Assert.True(R.Count(page, c => R.Near(c, SubtitleColor, 10), 0, sub.Y, 336, 20) > 30);
    });

    [Fact]
    public void One_category_has_a_solid_heading_band_and_the_items_below_it() => Sta.Run(() =>
    {
        var r = R.Render(R.Items(3));
        var ops = r.Layout.Pages[0].Ops;
        var band = ops.OfType<FillOp>().Single(f => f.Color == Accent && f.Height > RuleHeight);
        var page = r.Pages[0];
        Assert.Equal(Accent, page.GetPixel(band.X, band.Y));
        Assert.Equal(Accent, page.GetPixel(band.X + band.Width - 1, band.Y + band.Height - 1));
        Assert.True(R.Count(page, c => R.Near(c, Rgb.Black, 30), band.X + 4, band.Y, 60, band.Height) > 40);          // dark heading text on amber
        var firstItem = R.Texts(r.Layout.Pages[0]).First(t => t.Color == NameColor && t.Style == ItemName);
        Assert.True(firstItem.Y >= band.Y + band.Height);
    });

    [Fact]
    public void Multiple_categories_each_get_a_band_in_order() => Sta.Run(() =>
    {
        var r = R.Render(SampleMenus.FestivalFood());
        var bands = r.Layout.Pages[0].Ops.OfType<FillOp>().Where(f => f.Color == Accent && f.Height > RuleHeight && f.Width == 320).OrderBy(f => f.Y).ToList();
        Assert.Equal(3, bands.Count);
        Assert.All(bands, b => Assert.Equal(Accent, r.Pages[0].GetPixel(b.X + 2, b.Y + 1)));
    });

    [Fact]
    public void A_hidden_item_leaves_exactly_the_page_of_a_menu_without_it() => Sta.Run(() =>
    {
        var withHidden = SampleMenus.FestivalFood();
        var without = SampleMenus.FestivalFood();
        var victim = "Soft Pretzel";
        MenuEditor.SetVisible(withHidden, withHidden.Items.First(i => i.Name == victim).Id, false);
        without.Items.Remove(without.Items.First(i => i.Name == victim));
        Assert.True(R.SamePixels(R.Render(withHidden).Pages[0], R.Render(without).Pages[0]));
    });

    [Fact]
    public void A_hidden_category_leaves_exactly_the_page_of_a_menu_without_it() => Sta.Run(() =>
    {
        var hidden = SampleMenus.FestivalFood();
        var removed = SampleMenus.FestivalFood();
        MenuEditor.SetCategoryVisible(hidden, hidden.Categories.First(c => c.Name == "Appetizers").Id, false);
        MenuEditor.RemoveCategory(removed, removed.Categories.First(c => c.Name == "Appetizers").Id, deleteItems: true);
        Assert.True(R.SamePixels(R.Render(hidden).Pages[0], R.Render(removed).Pages[0]));
    });

    [Fact]
    public void A_sold_out_item_gets_a_red_badge_dimmed_text_and_a_strike_and_nothing_moves() => Sta.Run(() =>
    {
        var normal = SampleMenus.FestivalFood();
        var sold = SampleMenus.FestivalFood();
        var item = sold.Items.First(i => i.Name == "Veggie Burger");
        item.SoldOut = true;
        var rn = R.Render(normal);
        var rs = R.Render(sold);
        Assert.Equal(rn.PageCount, rs.PageCount);

        var name = R.Texts(rs.Layout.Pages[0]).Single(t => t.Text == "Veggie Burger");
        var rowY = name.Y;
        var page = rs.Pages[0];

        Assert.True(R.Count(page, c => R.Near(c, BadgeColor, 6), 220, rowY, 116, 26) > 300);                 // the red badge
        Assert.True(R.Count(page, c => R.Near(c, Rgb.White, 6), 250, rowY, 86, 26) > 60);                    // SOLD OUT in white inside it
        Assert.Equal(0, R.Count(page, c => c.R > 200 && c.G > 200 && c.B > 200, 8, rowY, 200, 24));           // the name is dimmed: no bright white
        Assert.True(R.Count(page, c => R.Near(c, StrikeColor, 6), 8, rowY, 200, 24) > 40);                    // struck through
        Assert.Equal(0, R.Count(page, c => R.Near(c, PriceColor, 20), 220, rowY, 116, 26));                   // the price is gone

        // everything above the sold-out row is untouched, pixel for pixel
        var above = rowY - 2;
        for (var y = 0; y < above; y++)
            for (var x = 0; x < 336; x++)
                Assert.Equal(rn.Pages[0].GetPixel(x, y), page.GetPixel(x, y));
    });

    [Fact]
    public void Several_sold_out_items_keep_the_page_orderly() => Sta.Run(() =>
    {
        var r = R.Render(SampleMenus.SoldOutDemo());
        var badges = r.Layout.Pages[0].Ops.OfType<FillOp>().Count(f => f.Color == BadgeColor);
        Assert.Equal(3, badges);
        // the badge color is rare: well under 6% of the page
        Assert.True(R.Count(r.Pages[0], c => R.Near(c, BadgeColor, 6)) < 336 * 672 * 0.06);
        Assert.Equal(1, r.PageCount);
    });

    [Fact]
    public void A_featured_item_gets_an_accent_bar_and_warmer_name_without_moving_anything() => Sta.Run(() =>
    {
        var plain = SampleMenus.FestivalFood();
        var featured = SampleMenus.FestivalFood();
        var target = featured.Items.First(i => i.Name == "Fries");
        target.Featured = true;
        var rp = R.Render(plain);
        var rf = R.Render(featured);
        Assert.Equal(rp.PageCount, rf.PageCount);

        var nameOp = R.Texts(rf.Layout.Pages[0]).Single(t => t.Text == "Fries");
        var bar = rf.Layout.Pages[0].Ops.OfType<FillOp>().Single(f => f.X == FeaturedBarX && f.Width == FeaturedBarWidth && f.Y == nameOp.Y);
        Assert.Equal(Accent, rf.Pages[0].GetPixel(bar.X, bar.Y));
        Assert.Equal(Accent, rf.Pages[0].GetPixel(bar.X + bar.Width - 1, bar.Y + bar.Height - 1));

        // the only pixels that differ lie inside this item's own rows
        for (var y = 0; y < 672; y++)
            for (var x = 0; x < 336; x++)
                if (rp.Pages[0].GetPixel(x, y) != rf.Pages[0].GetPixel(x, y))
                    Assert.InRange(y, bar.Y, bar.Y + bar.Height - 1);
    });

    [Fact]
    public void Drawn_text_never_extends_past_what_was_measured() => Sta.Run(() =>
    {
        var m = SampleMenus.FestivalFood();
        var r = R.Render(m);
        var measurer = new WpfTextMeasurer(R.Fonts.Resolve(null).Family);
        foreach (var op in R.Texts(r.Layout.Pages[0]).Where(t => t.Color == NameColor || t.Color == FeaturedNameColor || t.Color == DescriptionColor))
        {
            var width = (int)Math.Ceiling(measurer.Width(op.Text, op.Style));
            var h = (int)Math.Ceiling(measurer.LineHeight(op.Style));
            var b = R.Bounds(r.Pages[0], c => R.Near(c, op.Color, 40), op.X - 6, op.Y, width + 40, h);
            Assert.NotNull(b);
            // left and right extent of the glyph ink lies inside the measured box (allowing a pixel of side bearing)
            Assert.True(b!.Value.X0 >= op.X - 2, $"{op.Text}: ink starts at {b.Value.X0}, box starts at {op.X}");
            Assert.True(b.Value.X1 <= op.X + width + 2, $"{op.Text}: ink ends at {b.Value.X1}, box ends at {op.X + width}");
        }
    });
}

public class RendererLogoTests
{
    private static readonly Color Magenta = Color.FromRgb(255, 0, 255);
    private static bool IsMagenta(Rgb c) => c.R > 250 && c.G < 6 && c.B > 250;

    [Fact]
    public void No_logo_draws_none_and_reserves_no_space() => Sta.Run(() =>
    {
        var plain = R.Render(SampleMenus.FestivalFood());
        Assert.Equal(0, R.Count(plain.Pages[0], IsMagenta));
        Assert.DoesNotContain(plain.Layout.Pages[0].Ops, o => o is LogoOp);
        Assert.Empty(plain.Problems);
    });

    [Fact]
    public void A_logo_is_drawn_inside_its_box_and_pushes_the_content_down() => Sta.Run(() =>
    {
        var path = R.MakeLogo(300, 100, Magenta);
        var m = SampleMenus.FestivalFood();
        m.LogoAsset = "logo.png";
        var plain = R.Render(SampleMenus.FestivalFood());
        var branded = R.Render(m, path);

        var op = branded.Layout.Pages[0].Ops.OfType<LogoOp>().Single();
        Assert.Equal((192, 64), (op.Width, op.Height));                      // 3:1 fitted into 320 x 64
        var b = R.Bounds(branded.Pages[0], IsMagenta);
        Assert.NotNull(b);
        Assert.InRange(b!.Value.X0, op.X, op.X + 1);
        Assert.InRange(b.Value.Y0, op.Y, op.Y + 1);
        Assert.InRange(b.Value.X1, op.X + op.Width - 2, op.X + op.Width - 1);
        Assert.InRange(b.Value.Y1, op.Y + op.Height - 2, op.Y + op.Height - 1);

        var plainBand = plain.Layout.Pages[0].Ops.OfType<FillOp>().First(f => f.Color == Accent && f.Height > RuleHeight);
        var brandedBand = branded.Layout.Pages[0].Ops.OfType<FillOp>().First(f => f.Color == Accent && f.Height > RuleHeight);
        Assert.Equal(LogoMaxHeight + LogoGap, brandedBand.Y - plainBand.Y);
    });

    [Theory]
    [InlineData(300, 100)]
    [InlineData(800, 100)]
    [InlineData(40, 160)]
    [InlineData(100, 100)]
    public void Logos_are_never_stretched(int lw, int lh) => Sta.Run(() =>
    {
        var path = R.MakeLogo(lw, lh, Magenta);
        var m = R.Items(2);
        m.LogoAsset = "logo.png";
        var r = R.Render(m, path);
        var b = R.Bounds(r.Pages[0], IsMagenta)!.Value;
        var w = b.X1 - b.X0 + 1;
        var h = b.Y1 - b.Y0 + 1;
        Assert.InRange((double)w / h, (double)lw / lh * 0.94, (double)lw / lh * 1.06);
        Assert.True(w <= 320 && h <= LogoMaxHeight);
        Assert.True(w == 320 || h == LogoMaxHeight || h >= LogoMaxHeight - 1 || w >= 319);   // as large as the box allows
    });

    [Fact]
    public void The_logo_repeats_on_every_page() => Sta.Run(() =>
    {
        var path = R.MakeLogo(300, 100, Magenta);
        var m = SampleMenus.DenseMenu();
        m.LogoAsset = "logo.png";
        var r = R.Render(m, path);
        Assert.True(r.PageCount > 1);
        Assert.All(r.Pages, p => Assert.True(R.Count(p, IsMagenta) > 5000));
    });

    [Fact]
    public void A_missing_logo_renders_the_menu_without_it_and_reports_a_warning() => Sta.Run(() =>
    {
        var m = SampleMenus.FestivalFood();
        m.LogoAsset = "gone.png";
        var r = R.Render(m, logoPath: null);

        var problem = Assert.Single(r.Problems);
        Assert.Equal(RenderProblemKind.LogoMissing, problem.Kind);
        Assert.Contains("gone.png", problem.Message);
        Assert.DoesNotContain(r.Layout.Pages[0].Ops, o => o is LogoOp);

        // identical to a menu that never had a logo: the space is reclaimed, not left blank
        var reference = SampleMenus.FestivalFood();
        Assert.True(R.SamePixels(r.Pages[0], R.Render(reference).Pages[0]));
    });

    [Fact]
    public void An_unreadable_logo_does_not_crash_and_is_reported() => Sta.Run(() =>
    {
        var m = SampleMenus.FestivalFood();
        m.LogoAsset = "broken.png";
        var r = R.Render(m, R.MakeBrokenLogo());
        Assert.Equal(RenderProblemKind.LogoUnreadable, Assert.Single(r.Problems).Kind);
        Assert.True(R.SamePixels(r.Pages[0], R.Render(SampleMenus.FestivalFood()).Pages[0]));
    });

    [Fact]
    public void A_logo_file_that_does_not_exist_on_disk_is_reported_not_thrown() => Sta.Run(() =>
    {
        var m = SampleMenus.FestivalFood();
        m.LogoAsset = "x.png";
        var r = R.Render(m, Path.Combine(Path.GetTempPath(), "definitely-not-here.png"));
        Assert.Equal(RenderProblemKind.LogoUnreadable, Assert.Single(r.Problems).Kind);
    });
}

public class RendererLongContentTests
{
    [Fact]
    public void Long_names_descriptions_and_prices_wrap_and_stay_inside_the_screen() => Sta.Run(() =>
    {
        var r = R.Render(SampleMenus.LongTextStress());
        Assert.Empty(r.Problems);
        foreach (var page in r.Pages)
        {
            Assert.Equal(0, R.Count(page, R.NotBlack, 0, 0, 1, 672));
            Assert.Equal(0, R.Count(page, R.NotBlack, 331, 0, 5, 672));
        }
        // the long name was split over several lines, in order, with nothing lost
        var lines = R.Texts(r.Layout.Pages[0]).Where(t => t.Color == NameColor && t.Style == ItemName && t.Y < 200).Select(t => t.Text).ToList();
        Assert.Contains("Smoked Brisket and Pulled", string.Join(" ", lines));
    });

    [Fact]
    public void A_long_price_sits_on_its_own_line_right_aligned() => Sta.Run(() =>
    {
        var r = R.Render(SampleMenus.LongTextStress());
        var price = R.Texts(r.Layout.Pages[0]).Single(t => t.Text == "Market Price (ask us)");
        var name = R.Texts(r.Layout.Pages[0]).First(t => t.Text.StartsWith("Supercali"));
        Assert.True(price.Y > name.Y);
        var measurer = new WpfTextMeasurer(R.Fonts.Resolve(null).Family);
        Assert.Equal(328, price.X + (int)Math.Ceiling(measurer.Width(price.Text, price.Style)));
    });

    [Fact]
    public void A_title_too_long_for_two_lines_still_fits_the_screen_width() => Sta.Run(() =>
    {
        var m = new Menu { HeaderText = "THE GREAT ANNUAL FESTIVAL OF FOOD AND DRINK AND MUSIC AND FUN" };
        var r = R.Render(m);
        Assert.Equal(0, R.Count(r.Pages[0], R.NotBlack, 331, 0, 5, 672));
        Assert.Equal(0, R.Count(r.Pages[0], R.NotBlack, 0, 0, 4, 672));
    });
}

public class RendererPaginationTests
{
    [Fact]
    public void A_menu_that_fits_is_one_page_with_no_page_number() => Sta.Run(() =>
    {
        var r = R.Render(SampleMenus.FestivalFood());
        Assert.Equal(1, r.PageCount);
        Assert.Equal(0, R.Count(r.Pages[0], R.NotBlack, 280, 645, 56, 27));   // the page-number corner is empty
    });

    [Fact]
    public void The_boundary_between_one_and_two_pages_comes_from_measured_text() => Sta.Run(() =>
    {
        var n = 1;
        while (R.Render(R.Items(n)).PageCount == 1) n++;
        var one = R.Render(R.Items(n - 1));
        var two = R.Render(R.Items(n));
        Assert.Equal(1, one.PageCount);
        Assert.Equal(2, two.PageCount);
        // the last line of the one-page menu is above the bottom margin; page number appears only on the two-page menu
        var lastBottom = one.Layout.Pages[0].Ops.OfType<TextOp>().Max(t => t.Y);
        Assert.True(lastBottom < 672 - MarginBottom);
        Assert.True(R.Count(two.Pages[0], c => R.Near(c, IndicatorColor, 60), 280, 645, 56, 27) > 8);
        Assert.True(R.Count(two.Pages[1], c => R.Near(c, IndicatorColor, 60), 280, 645, 56, 27) > 8);
    });

    [Fact]
    public void A_dense_menu_takes_several_pages_and_every_item_is_drawn_once() => Sta.Run(() =>
    {
        var m = SampleMenus.DenseMenu();
        var r = R.Render(m);
        Assert.Equal(3, r.PageCount);
        Assert.Empty(r.Problems);
        var drawn = r.Layout.Pages.SelectMany(p => R.Texts(p)).Where(t => t.Color == NameColor && t.Style == ItemName).Select(t => t.Text).ToList();
        Assert.Equal(m.Items.Count, drawn.Count);
        Assert.Equal(m.Items.Select(i => i.Name).OrderBy(x => x), drawn.OrderBy(x => x));
    });

    [Fact]
    public void A_category_crossing_a_page_boundary_continues_under_a_marked_heading() => Sta.Run(() =>
    {
        var r = R.Render(SampleMenus.DenseMenu());
        var headings = r.Layout.Pages.Select(p => R.Texts(p).Where(t => t.Color == HeadingTextColor).Select(t => t.Text).ToList()).ToList();
        Assert.Contains(headings[1], h => h.EndsWith("(CONT.)"));
        // that heading is the first thing under the header on the page
        var contHeading = R.Texts(r.Layout.Pages[1]).First(t => t.Text.EndsWith("(CONT.)"));
        var firstName = R.Texts(r.Layout.Pages[1]).First(t => t.Color == NameColor && t.Style == ItemName);
        Assert.True(contHeading.Y < firstName.Y);
    });

    [Fact]
    public void A_category_heading_is_never_left_without_an_item_below_it_on_the_same_page() => Sta.Run(() =>
    {
        for (var a = 1; a <= 12; a++)
            for (var b = 1; b <= 5; b++)
            {
                var m = new Menu { HeaderText = "T" };
                var c1 = MenuEditor.AddCategory(m, "First");
                var c2 = MenuEditor.AddCategory(m, "Second");
                for (var i = 0; i < a; i++) MenuEditor.AddItem(m, c1.Id, $"A{i}", "$1", "Short description");
                for (var i = 0; i < b + 6; i++) MenuEditor.AddItem(m, c2.Id, $"B{i}", "$2", i % 2 == 0 ? "A longer description line for this item" : "");
                var r = R.Render(m);
                foreach (var page in r.Layout.Pages)
                    foreach (var h in R.Texts(page).Where(t => t.Color == HeadingTextColor))
                        Assert.Contains(R.Texts(page), t => t.Color is { } c && (c == NameColor) && t.Style == ItemName && t.Y > h.Y);
            }
    });

    [Fact]
    public void Hiding_items_shortens_the_menu_and_showing_them_restores_it_exactly() => Sta.Run(() =>
    {
        var m = SampleMenus.DenseMenu();
        var original = R.Render(m);
        Assert.Equal(3, original.PageCount);

        var hidden = m.Items.Where(i => i.Name is "Smash Burger" or "Fries" or "Churros" or "Craft Soda" or "Baked Beans" or "Iced Tea"
                                         or "Veggie Wrap" or "Fried Oreos").ToList();
        foreach (var i in hidden) MenuEditor.SetVisible(m, i.Id, false);
        var reduced = R.Render(m);
        Assert.True(reduced.PageCount <= original.PageCount);
        foreach (var i in hidden)
            Assert.DoesNotContain(reduced.Layout.Pages.SelectMany(p => R.Texts(p)), t => t.Text == i.Name);
        Assert.True(reduced.Layout.Pages.SelectMany(p => p.Ops).Count() < original.Layout.Pages.SelectMany(p => p.Ops).Count());

        foreach (var i in hidden) MenuEditor.SetVisible(m, i.Id, true);
        var restored = R.Render(m);
        Assert.Equal(original.PageCount, restored.PageCount);
        for (var p = 0; p < original.PageCount; p++) Assert.True(R.SamePixels(original.Pages[p], restored.Pages[p]));
    });

    [Fact]
    public void Hiding_enough_items_drops_a_page() => Sta.Run(() =>
    {
        var m = SampleMenus.DenseMenu();
        var before = R.Render(m).PageCount;
        foreach (var i in m.Items.Where(i => i.Name != "Lemonade").Skip(2)) MenuEditor.SetVisible(m, i.Id, false);
        var after = R.Render(m).PageCount;
        Assert.True(after < before);
        Assert.Equal(1, after);
    });
}

public class RendererOverflowTests
{
    [Fact]
    public void An_item_that_cannot_fit_a_page_is_reported_and_not_drawn_or_clipped() => Sta.Run(() =>
    {
        var m = R.Items(3);
        m.Items[1].Name = "Giant Platter";
        m.Items[1].Description = string.Join(" ", Enumerable.Repeat("an extremely long description that never ends", 120));
        var r = R.Render(m);

        var problem = Assert.Single(r.Problems);
        Assert.Equal(RenderProblemKind.ItemOverflow, problem.Kind);
        Assert.Contains("Giant Platter", problem.Message);
        Assert.Equal(1, r.PageCount);
        Assert.DoesNotContain(R.Texts(r.Layout.Pages[0]), t => t.Text.Contains("Giant") || t.Text.Contains("extremely"));
        Assert.Contains(R.Texts(r.Layout.Pages[0]), t => t.Text == "Item 1");
        Assert.Contains(R.Texts(r.Layout.Pages[0]), t => t.Text == "Item 3");
        // no half-drawn item: the lowest ink on the page is the last real item, well above the bottom
        Assert.Equal(0, R.Count(r.Pages[0], R.NotBlack, 0, 640, 336, 32));
    });

    [Fact]
    public void The_overflow_warning_goes_away_when_the_item_is_hidden() => Sta.Run(() =>
    {
        var m = R.Items(2);
        m.Items[0].Description = string.Join(" ", Enumerable.Repeat("endless description words", 150));
        Assert.Single(R.Render(m).Problems);
        MenuEditor.SetVisible(m, m.Items[0].Id, false);
        Assert.Empty(R.Render(m).Problems);
    });
}

public class RendererFontTests
{
    [Fact]
    public void The_bundled_font_is_available_and_used_by_default() => Sta.Run(() =>
    {
        Assert.Contains("Lato", R.Fonts.Families);
        var choice = R.Fonts.Resolve(null);
        Assert.Equal("Lato", choice.Name);
        Assert.Null(choice.Warning);
        Assert.Null(R.Fonts.Resolve("lato").Warning);          // case-insensitive
    });

    [Fact]
    public void An_unknown_font_falls_back_to_the_default_with_a_warning_and_still_renders() => Sta.Run(() =>
    {
        var m = SampleMenus.FestivalFood();
        m.Theme.FontFamily = "Brand Font Not Installed";
        var r = R.Render(m);
        var problem = Assert.Single(r.Problems);
        Assert.Equal(RenderProblemKind.FontFallback, problem.Kind);
        Assert.Contains("Brand Font Not Installed", problem.Message);
        Assert.True(R.SamePixels(r.Pages[0], R.Render(SampleMenus.FestivalFood()).Pages[0]));   // exactly the default look
    });

    [Fact]
    public void A_missing_fonts_folder_falls_back_to_a_system_font_with_a_warning() => Sta.Run(() =>
    {
        var none = new FontCatalog(Path.Combine(Path.GetTempPath(), "no-such-fonts-folder"));
        var choice = none.Resolve(null);
        Assert.NotNull(choice.Warning);
        var r = MenuRenderer.Render(SampleMenus.FestivalFood(), 336, 672, none, null);
        Assert.Contains(r.Problems, p => p.Kind == RenderProblemKind.FontFallback);
        Assert.Equal(336, r.Pages[0].Width);
    });

    [Fact]
    public void An_unknown_template_falls_back_with_a_warning() => Sta.Run(() =>
    {
        var m = SampleMenus.FestivalFood();
        m.Theme.TemplateId = "landscape-fancy";
        var r = R.Render(m);
        Assert.Contains(r.Problems, p => p.Kind == RenderProblemKind.TemplateFallback);
        Assert.Equal(1, r.PageCount);
    });

    [Fact]
    public void Bold_text_is_heavier_than_regular_text_of_the_same_size() => Sta.Run(() =>
    {
        var measurer = new WpfTextMeasurer(R.Fonts.Resolve(null).Family);
        var regular = measurer.Width("Black & Blue Burger", new TextStyle(20, FontWeightKind.Regular));
        var bold = measurer.Width("Black & Blue Burger", new TextStyle(20, FontWeightKind.Bold));
        Assert.True(bold > regular);
    });
}

public class RendererOutputIntegrationTests
{
    [Fact]
    public void A_rendered_page_lands_on_the_output_canvas_exactly_at_the_screens_coordinates() => Sta.Run(() =>
    {
        var page = R.Render(SampleMenus.FestivalFood()).Pages[0];
        foreach (var (x, y) in new[] { (0, 0), (372, 204), (1584, 408) })
        {
            var frame = FrameComposer.ComposeScreens(1920, 1080, new[] { (x, y, page) });
            Assert.Equal((1920, 1080), (frame.Width, frame.Height));
            for (var py = 0; py < page.Height; py += 7)
                for (var px = 0; px < page.Width; px++)
                    Assert.Equal(page.GetPixel(px, py), frame.GetPixel(x + px, y + py));
            // outside the screen: pure black
            Assert.Equal(Rgb.Black, frame.GetPixel(x > 0 ? x - 1 : 1900, y));
            Assert.Equal(0, R.Count(frame, R.NotBlack, x + 336, y, 20, 672));
        }
    });

    [Fact]
    public void Two_screens_with_different_menus_are_independent() => Sta.Run(() =>
    {
        var a = R.Render(SampleMenus.FestivalFood()).Pages[0];
        var b = R.Render(SampleMenus.DenseMenu()).Pages[0];
        var frame = FrameComposer.ComposeScreens(1920, 1080, new[] { (0, 0, a), (336, 0, b) });
        for (var y = 0; y < 672; y += 5)
            for (var x = 0; x < 336; x++)
            {
                Assert.Equal(a.GetPixel(x, y), frame.GetPixel(x, y));
                Assert.Equal(b.GetPixel(x, y), frame.GetPixel(336 + x, y));
            }
    });
}
