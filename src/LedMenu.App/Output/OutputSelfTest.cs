using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Interop;
using LedMenu.App.ViewModels;
using LedMenu.Core.Logging;
using static LedMenu.App.Display.NativeMethods;

namespace LedMenu.App.Output;

/// <summary>
/// Optional diagnostic run from the command line: <c>LedMenu.App.exe --selftest-output report.txt</c>.
/// It starts and stops output twice on the saved LED display, paints a known pixel pattern, captures the
/// real screen region (physical pixels) and compares every pixel. It proves that the window covers the
/// display exactly, that nothing (taskbar, desktop, rounded corners) shows through, and that the
/// operator's configuration file is unchanged by starting and stopping. It changes no settings.
/// </summary>
public static class OutputSelfTest
{
    public static async Task RunAsync(
        OutputViewModel output, OutputController controller, string settingsFile,
        IntPtr operatorHwnd, string reportPath, IAppLog log)
    {
        var sb = new StringBuilder();
        void Line(string s) { sb.AppendLine(s); }

        try
        {
            Line($"Output self-test {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            var before = Hash(settingsFile);
            Line($"Settings hash before: {before}");

            for (var cycle = 1; cycle <= 2; cycle++)
            {
                Line("");
                Line($"=== Cycle {cycle}: Start Output ===");
                output.Start();
                await Task.Delay(400);
                if (!controller.IsRunning)
                {
                    Line($"Output did NOT start. Message: {output.Message}");
                    continue;
                }

                var window = controller.Window!;
                var display = controller.Display!;
                window.ShowProbe();
                await Task.Delay(1500);   // let the compositor present the frame

                var r = window.BuildReport();
                Line($"1. Windows physical bounds of output display: {r.ExpectedBounds}  ({display.FriendlyName}, {display.DeviceName})");
                Line($"2. WPF DPI seen by output window: {r.WpfDpiX:0.##} x {r.WpfDpiY:0.##}  (scale {r.WpfDpiX / 96:0.##}); Windows monitor DPI: {r.MonitorDpiX} x {r.MonitorDpiY}");
                Line($"3. Window rectangle: {r.WindowRect}; client area: {r.ClientWidth} x {r.ClientHeight} physical px");
                Line($"4. Geometry agrees: {r.Agrees}" + (r.Agrees ? "" : "  PROBLEMS: " + string.Join(" | ", r.Problems)));
                Line($"   Output window IsActive: {window.IsActive}; foreground window is output: {GetForegroundWindow() == window.Hwnd}; foreground is operator: {GetForegroundWindow() == operatorHwnd}");

                var (mismatches, samples, interiorBlack, interiorTotal) = CaptureAndCompare(r.ExpectedBounds.X, r.ExpectedBounds.Y, r.ExpectedBounds.Width, r.ExpectedBounds.Height);
                Line($"   Pixel capture: {mismatches} mismatching pixels out of {(long)r.ExpectedBounds.Width * r.ExpectedBounds.Height}; " +
                     $"interior pure #000000: {interiorBlack}/{interiorTotal}");
                foreach (var s in samples) Line("     " + s);

                Line($"=== Cycle {cycle}: Stop Output ===");
                output.Stop("self-test");
                await Task.Delay(500);
                Line($"   Running after stop: {controller.IsRunning}");
            }

            var after = Hash(settingsFile);
            Line("");
            Line($"Settings hash after:  {after}");
            Line($"Settings file unchanged by start/stop cycles: {before == after}");
        }
        catch (Exception ex)
        {
            Line("SELF-TEST FAILED WITH EXCEPTION: " + ex);
            log.Error("Output self-test failed.", ex);
        }
        finally
        {
            File.WriteAllText(reportPath, sb.ToString());
            log.Info("Output self-test finished; report written.");
        }
    }

    private static string Hash(string file) =>
        File.Exists(file) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))[..16] : "(no file)";

    private static (long mismatches, List<string> samples, long interiorBlack, long interiorTotal)
        CaptureAndCompare(int x, int y, int w, int h)
    {
        var px = ScreenCapture.Capture(x, y, w, h);
        var samples = new List<string>();
        long mismatches = 0, interiorBlack = 0, interiorTotal = 0;
        for (var yy = 0; yy < h; yy++)
            for (var xx = 0; xx < w; xx++)
            {
                var i = (yy * w + xx) * 4;
                byte b = px[i], g = px[i + 1], r = px[i + 2];
                var (eb, eg, er) = ProbePattern.ExpectedAt(xx, yy, w, h);
                var interior = xx > 0 && yy > 0 && xx < w - 1 && yy < h - 1;
                if (interior) { interiorTotal++; if (b == 0 && g == 0 && r == 0) interiorBlack++; }
                if (b != eb || g != eg || r != er)
                {
                    mismatches++;
                    if (samples.Count < 8)
                        samples.Add($"mismatch at ({xx},{yy}): expected RGB({er},{eg},{eb}) got RGB({r},{g},{b})");
                }
            }
        return (mismatches, samples, interiorBlack, interiorTotal);
    }
}
