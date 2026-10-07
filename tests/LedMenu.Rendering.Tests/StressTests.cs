using System.Diagnostics;
using System.Windows.Media;
using LedMenu.Core.Menus;

namespace LedMenu.Rendering.Tests;

/// <summary>Hostile and enormous input: the wall must still get a correctly sized picture, quickly, and nothing may throw.</summary>
public class StressTests
{
    private static Menu MenuWith(params string[] names)
    {
        var cat = new MenuCategory { Name = "Things" };
        var m = new Menu { Name = "Stress", HeaderText = "STRESS" };
        m.Categories.Add(cat);
        var i = 0;
        foreach (var n in names)
            m.Items.Add(new MenuItem { Name = n, Price = "$" + i, CategoryId = cat.Id, SortOrder = i++ });
        return m;
    }

    private static void AssertSane(MenuRenderResult r, int w, int h)
    {
        Assert.True(r.PageCount >= 1);
        foreach (var p in r.Pages) { Assert.Equal(w, p.Width); Assert.Equal(h, p.Height); }
    }

    [Fact]
    public void A_thousand_items_across_forty_categories_render_in_reasonable_time_and_every_page_is_the_screen_size() => Sta.Run(() =>
    {
        var m = new Menu { Name = "Huge", HeaderText = "HUGE" };
        for (var c = 0; c < 40; c++)
        {
            var cat = new MenuCategory { Name = "Category " + c, SortOrder = c };
            m.Categories.Add(cat);
            for (var i = 0; i < 25; i++)
                m.Items.Add(new MenuItem { Name = $"Item {c}-{i} with a middling name", Description = "A short description of it", Price = "$" + (i + 1), CategoryId = cat.Id, SortOrder = c * 25 + i });
        }
        var sw = Stopwatch.StartNew();
        var r = R.Render(m);
        sw.Stop();
        AssertSane(r, 336, 672);
        Assert.True(r.PageCount > 20, $"expected many pages, got {r.PageCount}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
    });

    public static IEnumerable<object[]> HostileNames() => new[]
    {
        new object[] { "Pizza 🍕 Slice 🔥🔥🔥" },
        new object[] { "Crème brûlée à la façon de São Paulo — ñandú" },
        new object[] { "é́́́́́́́́́́́" },        // stacked combining marks
        new object[] { "برجر لحم بقري مع جبنة" },                                                               // Arabic (right to left)
        new object[] { "המבורגר עם גבינה" },                                                                    // Hebrew
        new object[] { "ハンバーガー チーズ 特大サイズ" },                                                          // Japanese
        new object[] { "Tab\there\tand\u0000null\u0007bell" },
        new object[] { "Line one\r\nLine two\nLine three" },                                                    // newlines inside a name
        new object[] { "‮reversed text override" },
        new object[] { "​​​​​​​​" },                                    // zero-width spaces only
        new object[] { "   " },
        new object[] { "" },
        new object[] { new string('W', 5000) },                                                                 // one 5000-character word
        new object[] { string.Join(" ", Enumerable.Repeat("supercalifragilistic", 300)) },                      // a paragraph in a name
        new object[] { "<b>markup</b> & \"quotes\" 'and' {braces} %s %d \\ /" },
    };

    [Theory]
    [MemberData(nameof(HostileNames))]
    public void Hostile_text_in_a_name_never_throws_and_still_gives_full_size_pages(string name) => Sta.Run(() =>
    {
        foreach (var (w, h) in new[] { (336, 672), (168, 672) })
        {
            var m = MenuWith("Ordinary item", name, "Another ordinary item");
            m.Items[1].Description = name;
            m.Items[1].Price = name.Length > 40 ? name[..40] : name;
            AssertSane(R.Render(m, w: w, h: h), w, h);
        }
    });

    [Fact]
    public void Hostile_text_in_the_header_subtitle_and_category_names_is_survivable() => Sta.Run(() =>
    {
        var m = MenuWith("One");
        m.HeaderText = "🍕" + new string('M', 800);
        m.Subtitle = "برجر\r\n\u0000" + new string('x', 800);
        m.Categories[0].Name = "‮" + new string('Ö', 600);
        AssertSane(R.Render(m), 336, 672);
        AssertSane(R.Render(m, w: 168, h: 672), 168, 672);
    });

    [Fact]
    public void A_price_of_five_thousand_characters_is_contained() => Sta.Run(() =>
    {
        var m = MenuWith("Item");
        m.Items[0].Price = new string('9', 5000);
        AssertSane(R.Render(m), 336, 672);
    });

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(168, 8)]
    [InlineData(8, 672)]
    [InlineData(4000, 4000)]
    public void Absurd_screen_sizes_do_not_crash_the_renderer(int w, int h) => Sta.Run(() =>
    {
        var r = R.Render(MenuWith("One", "Two", "Three"), w: w, h: h);
        AssertSane(r, w, h);
    });

    [Theory]
    [InlineData(6000, 6000)]     // 36 megapixel photo
    [InlineData(1, 1)]
    [InlineData(10000, 8)]       // extreme banner
    [InlineData(8, 10000)]       // extreme column
    public void Extreme_logo_shapes_and_sizes_render_and_stay_inside_the_screen(int lw, int lh) => Sta.Run(() =>
    {
        var logo = R.MakeLogo(lw, lh, Colors.OrangeRed, $"stress-logo-{lw}x{lh}.png");
        var m = MenuWith("One", "Two");
        var sw = Stopwatch.StartNew();
        var r = R.Render(m, logo);
        AssertSane(r, 336, 672);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"took {sw.Elapsed}");
        // the logo colour must not leak outside its screen: the picture is the screen size by construction; check it is not all logo
        var logoPixels = 0;
        var d = r.Pages[0].Data;
        for (var i = 0; i < d.Length; i += 4)
            if (d[i + 2] == 255 && d[i + 1] == 69 && d[i] == 0) logoPixels++;
        Assert.True(logoPixels < 336 * 672 / 2, "the logo must not swamp the menu");
    });

    [Fact]
    public void Rendering_the_same_big_menu_twice_gives_identical_pixels() => Sta.Run(() =>
    {
        var names = Enumerable.Range(0, 120).Select(i => $"Repeatable item number {i}").ToArray();
        var a = R.Render(MenuWith(names));
        var b = R.Render(MenuWith(names));
        Assert.Equal(a.PageCount, b.PageCount);
        for (var p = 0; p < a.PageCount; p++) Assert.Equal(a.Pages[p].Data, b.Pages[p].Data);
    });
}
