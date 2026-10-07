using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedMenu.Core.Calibration;
using LedMenu.Core.Logging;
using LedMenu.Core.Menus;
using LedMenu.Core.Screens;
using LedMenu.Persistence;
using LedMenu.Rendering;

namespace LedMenu.App.Output;

/// <summary>
/// <c>LedMenu.App.exe --dump-calibration &lt;folder&gt; [WIDTHxHEIGHT]</c> writes the test patterns as PNG files
/// (default canvas 1920x1080, using the saved screens). Useful for reference when reading photos of the LED wall.
/// </summary>
public static class PatternDump
{
    public static void Write(string folder, int width, int height, ScreenLayout layout)
    {
        Directory.CreateDirectory(folder);
        var plan = CalibrationPlan.Build(layout.Screens, new CanvasSize(width, height));
        foreach (var mode in new[] { OutputMode.IdentifyScreens, OutputMode.ScreenCalibration, OutputMode.OutputCanvasCalibration })
        {
            var frame = FrameComposer.Compose(mode, width, height, plan.Drawn);
            Save(Path.Combine(folder, $"{mode}_{width}x{height}.png"), frame);
        }
        foreach (var s in plan.Drawn)
        {
            Save(Path.Combine(folder, $"Screen{s.Number}_calibration_{s.Width}x{s.Height}.png"), ScreenPatterns.Calibration(s));
            Save(Path.Combine(folder, $"Screen{s.Number}_identify_{s.Width}x{s.Height}.png"), ScreenPatterns.Identify(s));
        }
    }

    /// <summary>
    /// Renders every menu to PNG files, one per page, at its assigned screen's size (or 336x672). With a logo file, each menu
    /// is rendered a second time with that logo so the branded layout can be reviewed. Returns the number of files written.
    /// </summary>
    public static int WriteMenus(string folder, IEnumerable<Menu> menus, ScreenLayout layout, FontCatalog fonts,
        AssetStore assets, string? logoFile, IAppLog log, (int Width, int Height)? sizeOverride = null)
    {
        Directory.CreateDirectory(folder);
        var written = 0;
        var report = new System.Text.StringBuilder();
        var reportMeasurer = new WpfTextMeasurer(fonts.Resolve(null).Family);
        foreach (var menu in menus)
        {
            var screen = layout.Screens.FirstOrDefault(s => s.AssignedMenuId == menu.Id && s.Enabled && s.Width > 0 && s.Height > 0);
            int w = sizeOverride?.Width ?? screen?.Width ?? 336, h = sizeOverride?.Height ?? screen?.Height ?? 672;
            var safe = new string(menu.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

            var plain = MenuRenderer.Render(menu, w, h, fonts, assets.Resolve(menu.LogoAsset));
            for (var i = 0; i < plain.PageCount; i++) { Save(Path.Combine(folder, $"{safe}_p{i + 1}of{plain.PageCount}.png"), plain.Pages[i]); written++; }
            foreach (var p in plain.Problems) log.Warn($"{menu.Name}: {p.Message}");
            report.AppendLine(LayoutReport.Describe(menu.Name, plain, reportMeasurer));

            if (logoFile != null && File.Exists(logoFile))
            {
                var branded = System.Text.Json.JsonSerializer.Deserialize<Menu>(System.Text.Json.JsonSerializer.Serialize(menu))!;
                branded.LogoAsset = Path.GetFileName(logoFile);
                var withLogo = MenuRenderer.Render(branded, w, h, fonts, logoFile);
                for (var i = 0; i < withLogo.PageCount; i++) { Save(Path.Combine(folder, $"{safe}_WITHLOGO_p{i + 1}of{withLogo.PageCount}.png"), withLogo.Pages[i]); written++; }
            }
        }
        File.WriteAllText(Path.Combine(folder, $"layout-report_{(sizeOverride?.Width ?? 0)}x{(sizeOverride?.Height ?? 0)}.txt"), report.ToString());
        return written;
    }

    private static void Save(string path, PixelBuffer frame)
    {
        var bmp = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Data, frame.Width * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
