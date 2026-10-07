using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedMenu.Core.Calibration;
using LedMenu.Core.Screens;

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

    private static void Save(string path, PixelBuffer frame)
    {
        var bmp = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Data, frame.Width * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
