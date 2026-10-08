using System.IO;
using System.Text;
using LedMenu.App.ViewModels;
using LedMenu.Core.Calibration;
using LedMenu.Core.Logging;
using LedMenu.Core.Video;

namespace LedMenu.App.Output;

/// <summary>
/// Optional diagnostic: <c>LedMenu.App.exe --selftest-video report.txt</c>, run against a data folder that already holds a menu screen and a video screen.
/// It starts the real output window, lets the videos play, and every half second reads back what WPF actually holds in the output window's
/// surface and compares it, pixel by pixel, with the picture the program believes it is showing. That checks the whole path (decoder, player,
/// compositor, partial window refresh) without needing to photograph the screen. It changes no settings or data.
/// </summary>
public static class VideoSelfTest
{
    public static async Task RunAsync(OutputViewModel output, OutputController controller, string reportPath, IAppLog log, int seconds = 20)
    {
        var sb = new StringBuilder();
        void Line(string s) { sb.AppendLine(s); }

        try
        {
            Line($"Video self-test {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ({seconds} s)");
            output.Start();
            await Task.Delay(500);
            if (!controller.IsRunning)
            {
                Line($"Output did NOT start. Message: {output.Message}");
                return;
            }

            var display = controller.Display!;
            Line($"Output display {display.Width}x{display.Height}; canvas {output.Canvas}");
            var screens = output.DrawnScreens;
            foreach (var s in screens) Line($"  screen {s.Number} \"{s.Name}\" {s.Width}x{s.Height} at ({s.X},{s.Y}) {(s.IsVideo ? "VIDEO" : "menu")}");
            var videos = screens.Where(s => s.IsVideo).ToList();
            if (videos.Count == 0) { Line("No video screen is being drawn; nothing to test."); return; }

            var window = controller.Window!;
            var checks = 0; var perfect = 0; long worst = 0;
            var distinctVideoPictures = new HashSet<int>();
            var colourSamples = new List<string>();
            var blackWhilePlaying = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            var cpu0 = proc.TotalProcessorTime;

            while (clock.Elapsed.TotalSeconds < seconds)
            {
                await Task.Delay(500);
                var shown = window.CaptureSurface();
                var expected = output.LastFrame;
                if (shown == null || expected == null) { Line("  (no picture to compare yet)"); continue; }
                checks++;

                // 1. everything WPF holds in the window equals the picture the program thinks it is showing
                long diff = 0;
                var n = Math.Min(shown.Data.Length, expected.Data.Length);
                if (shown.Width != expected.Width || shown.Height != expected.Height) diff = long.MaxValue;
                else
                    for (var i = 0; i < n; i += 4)
                        if (shown.Data[i] != expected.Data[i] || shown.Data[i + 1] != expected.Data[i + 1] || shown.Data[i + 2] != expected.Data[i + 2]) diff++;
                if (diff == 0) perfect++;
                worst = Math.Max(worst, diff);

                // 2. the video screens are moving and are not black
                foreach (var v in videos)
                {
                    var h = new HashCode();
                    var lit = 0;
                    for (var y = v.Y; y < v.Y + v.Height; y += 5)
                        for (var x = v.X; x < v.X + v.Width; x += 5)
                        {
                            var i = (y * shown.Width + x) * 4;
                            h.Add(shown.Data[i]); h.Add(shown.Data[i + 1]); h.Add(shown.Data[i + 2]);
                            if (shown.Data[i] != 0 || shown.Data[i + 1] != 0 || shown.Data[i + 2] != 0) lit++;
                        }
                    distinctVideoPictures.Add(h.ToHashCode());
                    if (lit == 0) blackWhilePlaying++;
                }
                if (checks % 8 == 1) colourSamples.Add($"t={clock.Elapsed.TotalSeconds:0.0}s mismatching pixels {diff}");
            }

            var cpu = (proc.TotalProcessorTime - cpu0).TotalSeconds / clock.Elapsed.TotalSeconds * 100;
            Line("");
            Line($"Comparisons of the window's real content with the program's picture: {checks}; exact matches: {perfect}; worst mismatch: {worst} pixels");
            foreach (var c in colourSamples) Line("  " + c);
            Line($"Different video pictures seen in the window (sampled twice a second): {distinctVideoPictures.Count}");
            Line($"Checks where a video screen was completely black: {blackWhilePlaying}");
            Line($"Process CPU during the run: {cpu:0}% of one core");
            Line($"Problems shown to the operator: {(string.IsNullOrEmpty(output.ContentWarnings) ? "(none)" : output.ContentWarnings.Replace("\n", " | "))}");

            // ---- blackout, test patterns and back, with the videos still playing underneath ----
            long Mismatch(PixelBuffer? shown, PixelBuffer? expected)
            {
                if (shown == null || expected == null || shown.Width != expected.Width || shown.Height != expected.Height) return long.MaxValue;
                long d = 0;
                for (var i = 0; i < shown.Data.Length; i += 4)
                    if (shown.Data[i] != expected.Data[i] || shown.Data[i + 1] != expected.Data[i + 1] || shown.Data[i + 2] != expected.Data[i + 2]) d++;
                return d;
            }
            long Lit(PixelBuffer? p) { long n = 0; if (p != null) for (var i = 0; i < p.Data.Length; i += 4) if (p.Data[i] != 0 || p.Data[i + 1] != 0 || p.Data[i + 2] != 0) n++; return n; }
            Line("");
            output.ToggleBlackout();
            await Task.Delay(1200);
            Line($"Blackout ON: lit pixels in the output window = {Lit(window.CaptureSurface())} (expected 0); preview picture lit pixels = {Lit(output.CurrentFrame)} (expected 0)");
            output.ToggleBlackout();
            await Task.Delay(1200);
            Line($"Blackout OFF: window differs from the program's picture by {Mismatch(window.CaptureSurface(), output.LastFrame)} pixels (expected 0); video screen lit = {Lit(window.CaptureSurface()) > 0}");

            output.SetMode(LedMenu.Core.Calibration.OutputMode.ScreenCalibration);
            for (var i = 0; i < 100 && !output.FrameUpToDate; i++) await Task.Delay(100);
            await Task.Delay(1000);
            Line($"Screen Calibration pattern: window differs from the program's picture by {Mismatch(window.CaptureSurface(), output.LastFrame)} pixels (expected 0)");
            output.SetMode(LedMenu.Core.Calibration.OutputMode.Normal);
            await Task.Delay(2000);
            var back = window.CaptureSurface();
            Line($"Back to Normal Output: window differs from the program's picture by {Mismatch(back, output.LastFrame)} pixels (expected 0)");
            var videoLit = 0L;
            foreach (var v in videos) for (var y = v.Y; y < v.Y + v.Height; y += 5) for (var x = v.X; x < v.X + v.Width; x += 5) { var i = (y * back!.Width + x) * 4; if (back.Data[i] != 0 || back.Data[i + 1] != 0 || back.Data[i + 2] != 0) videoLit++; }
            Line($"  video screen is showing the video again: {videoLit > 0}");

            // stopping output returns everything to black and ends the players
            output.Stop("self-test");
            await Task.Delay(500);
            var after = output.LastFrame;
            var anyLit = false;
            if (after != null)
                foreach (var v in videos)
                    for (var y = v.Y; y < v.Y + v.Height && !anyLit; y += 5)
                        for (var x = v.X; x < v.X + v.Width; x += 5)
                        {
                            var i = (y * after.Width + x) * 4;
                            if (after.Data[i] != 0 || after.Data[i + 1] != 0 || after.Data[i + 2] != 0) { anyLit = true; break; }
                        }
            Line($"After Stop: output running={controller.IsRunning}; video screens black in the preview picture: {!anyLit}");
        }
        catch (Exception ex)
        {
            Line("VIDEO SELF-TEST FAILED WITH EXCEPTION: " + ex);
            log.Error("Video self-test failed.", ex);
        }
        finally
        {
            File.WriteAllText(reportPath, sb.ToString());
            log.Info("Video self-test finished; report written.");
        }
    }
}
