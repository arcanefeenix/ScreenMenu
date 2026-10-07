using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedMenu.Core.Calibration;
using LedMenu.Core.Layout;
using LedMenu.Core.Menus;

namespace LedMenu.Rendering;

public enum RenderProblemKind { LogoMissing, LogoUnreadable, FontFallback, TemplateFallback, ItemOverflow }

public sealed record RenderProblem(RenderProblemKind Kind, string Message);

/// <summary>
/// A rendered menu: the layout (which pages, what is on them) and one bitmap per page, each exactly the
/// screen's pixel size. The same pages are used for the LED output and for the operator's preview.
/// </summary>
public sealed record MenuRenderResult(
    int Width, int Height,
    MenuLayoutResult Layout,
    IReadOnlyList<PixelBuffer> Pages,
    IReadOnlyList<RenderProblem> Problems)
{
    public int PageCount => Pages.Count;
}

public sealed record LogoLoadResult(BitmapSource? Image, string? Error);

public static class LogoLoader
{
    /// <summary>Decodes an image file fully into memory (so the file is not left locked). Never throws.</summary>
    public static LogoLoadResult Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0) return new LogoLoadResult(null, "The image has no pixels.");
            frame.Freeze();
            return new LogoLoadResult(frame, null);
        }
        catch (Exception ex)
        {
            return new LogoLoadResult(null, ex.Message);
        }
    }
}

/// <summary>The single authoritative menu renderer. Must be called on a thread that can use WPF imaging (the UI thread or an STA thread).</summary>
public static class MenuRenderer
{
    /// <param name="logoPath">Full path of the menu's logo if the asset exists; null if there is none or it is missing.</param>
    public static MenuRenderResult Render(Menu menu, int width, int height, FontCatalog fonts, string? logoPath)
    {
        var problems = new List<RenderProblem>();

        // template
        if (!string.Equals(menu.Theme.TemplateId, PortraitBasicTemplate.Id, StringComparison.OrdinalIgnoreCase))
            problems.Add(new RenderProblem(RenderProblemKind.TemplateFallback,
                $"The template \"{menu.Theme.TemplateId}\" is not available, so \"{PortraitBasicTemplate.Id}\" is being used."));

        // font
        var font = fonts.Resolve(menu.Theme.FontFamily);
        if (font.Warning != null) problems.Add(new RenderProblem(RenderProblemKind.FontFallback, font.Warning));
        var measurer = new WpfTextMeasurer(font.Family);

        // logo: a missing or unreadable logo never stops the menu; its space is simply not reserved
        BitmapSource? logo = null;
        if (!string.IsNullOrWhiteSpace(menu.LogoAsset))
        {
            if (logoPath == null)
                problems.Add(new RenderProblem(RenderProblemKind.LogoMissing,
                    $"The logo \"{menu.LogoAsset}\" is missing. The menu is shown without it."));
            else
            {
                var loaded = LogoLoader.Load(logoPath);
                if (loaded.Image == null)
                    problems.Add(new RenderProblem(RenderProblemKind.LogoUnreadable,
                        $"The logo \"{menu.LogoAsset}\" could not be read ({loaded.Error}). The menu is shown without it."));
                else logo = loaded.Image;
            }
        }

        var view = MenuView.Build(menu);
        var layout = MenuLayoutEngine.Layout(view, menu.Theme, width, height,
            logo == null ? null : new LogoMetrics(logo.PixelWidth, logo.PixelHeight), measurer);
        foreach (var p in layout.Problems)
            problems.Add(new RenderProblem(RenderProblemKind.ItemOverflow, p.Message));

        var pages = layout.Pages.Select(page => DrawPage(page, width, height, measurer, logo)).ToList();
        return new MenuRenderResult(width, height, layout, pages, problems);
    }

    /// <summary>Draws one page into an exactly width x height bitmap at 96 DPI, so one WPF unit is one pixel.</summary>
    public static PixelBuffer DrawPage(MenuPage page, int width, int height, WpfTextMeasurer measurer, BitmapSource? logo)
    {
        var visual = new DrawingVisual();
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);              // rectangles land on whole pixels
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brush(PortraitBasicTemplate.Background), null, new Rect(0, 0, width, height));
            foreach (var op in page.Ops)
            {
                switch (op)
                {
                    case FillOp f:
                        dc.DrawRectangle(Brush(f.Color), null, new Rect(f.X, f.Y, f.Width, f.Height));
                        break;
                    case TextOp t:
                        dc.DrawText(measurer.Format(t.Text, t.Style, Brush(t.Color)), new Point(t.X, t.Y));
                        break;
                    case LogoOp l when logo != null:
                        dc.DrawImage(logo, new Rect(l.X, l.Y, l.Width, l.Height));
                        break;
                }
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var buffer = new PixelBuffer(width, height);
        target.CopyPixels(buffer.Data, width * 4, 0);
        for (var i = 3; i < buffer.Data.Length; i += 4) buffer.Data[i] = 255;   // the page is opaque
        return buffer;
    }

    private static Brush Brush(Rgb c)
    {
        var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
