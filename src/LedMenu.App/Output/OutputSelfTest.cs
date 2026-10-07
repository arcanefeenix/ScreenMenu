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

            // ---- every output mode: the frame the app composed must be on the real screen, pixel for pixel ----
            Line("");
            Line("=== Output modes ===");
            output.Start();
            await Task.Delay(400);
            if (!controller.IsRunning) Line($"Output did NOT start for the mode test. Message: {output.Message}");
            else
            {
                var display = controller.Display!;
                foreach (var mode in new[] { LedMenu.Core.Calibration.OutputMode.IdentifyScreens,
                                              LedMenu.Core.Calibration.OutputMode.ScreenCalibration,
                                              LedMenu.Core.Calibration.OutputMode.OutputCanvasCalibration,
                                              LedMenu.Core.Calibration.OutputMode.Normal })
                {
                    output.SetMode(mode);
                    for (var i = 0; i < 100 && !output.FrameUpToDate; i++) await Task.Delay(100);
                    await Task.Delay(1500);

                    var expected = output.LastFrame ?? new LedMenu.Core.Calibration.PixelBuffer(display.Width, display.Height);
                    var (mismatches, samples) = CompareToFrame(display.X, display.Y, expected);
                    Line($"{OutputViewModel.ModeName(mode)}: {mismatches} mismatching pixels out of {(long)display.Width * display.Height} " +
                         $"(frame {expected.Width}x{expected.Height}); banner=\"{output.ModeBannerText}\"");
                    foreach (var smp in samples) Line("     " + smp);
                    if (!string.IsNullOrEmpty(output.SkippedText)) Line("     " + output.SkippedText);
                }
                // ---- blackout: pure black on the real screen over a busy picture, and the picture comes back exactly ----
                Line("");
                Line("=== Blackout ===");
                output.SetMode(LedMenu.Core.Calibration.OutputMode.OutputCanvasCalibration);
                for (var i = 0; i < 100 && !output.FrameUpToDate; i++) await Task.Delay(100);
                await Task.Delay(1500);
                var busy = output.LastFrame ?? new LedMenu.Core.Calibration.PixelBuffer(display.Width, display.Height);
                var (m0, _) = CompareToFrame(display.X, display.Y, busy);
                Line($"Before blackout (canvas calibration pattern): {m0} mismatching pixels");

                output.ToggleBlackout();
                await Task.Delay(1500);
                var black = new LedMenu.Core.Calibration.PixelBuffer(display.Width, display.Height);
                var (m1, s1) = CompareToFrame(display.X, display.Y, black);
                Line($"During blackout: {m1} pixels not pure #000000 out of {(long)display.Width * display.Height}; chip=\"{_chipOf(output)}\"; IsBlackout={output.IsBlackout}; preview frame is black: {IsAllBlack(output.CurrentFrame)}");
                foreach (var smp in s1) Line("     " + smp);

                // the picture underneath keeps being the current one while blacked out; changing mode must not lift blackout
                output.SetMode(LedMenu.Core.Calibration.OutputMode.IdentifyScreens);
                for (var i = 0; i < 100 && !output.FrameUpToDate; i++) await Task.Delay(100);
                await Task.Delay(1500);
                var (m1b, _) = CompareToFrame(display.X, display.Y, black);
                Line($"Blackout still pure black after switching to Identify Screens underneath: {m1b} mismatching pixels");

                output.SetMode(LedMenu.Core.Calibration.OutputMode.OutputCanvasCalibration);
                for (var i = 0; i < 100 && !output.FrameUpToDate; i++) await Task.Delay(100);
                output.ToggleBlackout();
                await Task.Delay(1500);
                var (m2, s2) = CompareToFrame(display.X, display.Y, busy);
                Line($"After ending blackout: {m2} mismatching pixels against the pattern that was there before; IsBlackout={output.IsBlackout}");
                foreach (var smp in s2) Line("     " + smp);

                // stopping while blacked out must leave nothing waiting for the next start
                output.ToggleBlackout();
                await Task.Delay(500);
                output.Stop("self-test");
                await Task.Delay(500);
                Line($"After Stop while blacked out: IsBlackout={output.IsBlackout} (expected False)");
                output.Start();
                await Task.Delay(1500);
                if (controller.IsRunning)
                {
                    var (m3, _) = CompareToFrame(display.X, display.Y, output.LastFrame ?? new LedMenu.Core.Calibration.PixelBuffer(display.Width, display.Height));
                    Line($"Next start after a blackout shows the normal picture: {m3} mismatching pixels; IsBlackout={output.IsBlackout}");
                }
                output.Stop("self-test");
                await Task.Delay(500);
                Line($"After Stop the mode is {output.Mode} (expected Normal); running={controller.IsRunning}");
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

    /// <summary>Captures the real screen at (x, y) and compares it with every pixel of <paramref name="expected"/>.</summary>
    private static (long mismatches, List<string> samples) CompareToFrame(int x, int y, LedMenu.Core.Calibration.PixelBuffer expected)
    {
        var px = ScreenCapture.Capture(x, y, expected.Width, expected.Height);
        var samples = new List<string>();
        long mismatches = 0;
        for (var i = 0; i < expected.Data.Length; i += 4)
        {
            if (px[i] == expected.Data[i] && px[i + 1] == expected.Data[i + 1] && px[i + 2] == expected.Data[i + 2]) continue;
            mismatches++;
            if (samples.Count < 8)
            {
                var p = i / 4;
                samples.Add($"mismatch at ({p % expected.Width},{p / expected.Width}): expected RGB({expected.Data[i + 2]},{expected.Data[i + 1]},{expected.Data[i]}) got RGB({px[i + 2]},{px[i + 1]},{px[i]})");
            }
        }
        return (mismatches, samples);
    }

    private static string _chipOf(OutputViewModel o) => o.IsBlackout ? "OUTPUT: BLACKOUT" : "(not blacked out)";

    private static bool IsAllBlack(LedMenu.Core.Calibration.PixelBuffer f)
    {
        for (var i = 0; i < f.Data.Length; i += 4)
            if (f.Data[i] != 0 || f.Data[i + 1] != 0 || f.Data[i + 2] != 0) return false;
        return true;
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
