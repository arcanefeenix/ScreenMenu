using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Usage: VideoSpike <mediaDir> <reportFile>
// For every media file: open with the built-in Windows decoder (WPF MediaPlayer), play muted for a few seconds, grab
// frames the way the output pipeline would (DrawVideo into a RenderTargetBitmap of the screen size), and report.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var dir = args[0];
        var report = new List<string>();
        void Line(string s) { report.Add(s); Console.WriteLine(s); }

        var dispatcher = Dispatcher.CurrentDispatcher;
        var files = Directory.GetFiles(dir, "*.mp4").OrderBy(f => f).ToArray();
        RunAll(files, Line);
        File.WriteAllLines(args[1], report);
        return 0;
    }

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void RunAll(string[] files, Action<string> line)
    {
        line($"VideoSpike {DateTime.Now:yyyy-MM-dd HH:mm:ss}   OS {Environment.OSVersion.Version}");
        foreach (var f in files) One(f, line);
    }

    private static void One(string path, Action<string> line)
    {
        line("");
        line($"=== {Path.GetFileName(path)} ({new FileInfo(path).Length / 1024} KB) ===");
        var player = new MediaPlayer { IsMuted = true, Volume = 0 };
        bool? opened = null;
        string? failure = null;
        player.MediaOpened += (_, _) => opened ??= true;
        player.MediaFailed += (_, e) => { failure = e.ErrorException?.Message ?? "failed"; opened ??= false; };
        var sw = Stopwatch.StartNew();
        player.Open(new Uri(path));

        PumpUntil(() => opened != null, 5000);
        if (opened == null) { line("Result: NEVER OPENED within 5 s (no MediaOpened, no MediaFailed)"); player.Close(); return; }
        if (opened == false) { line($"Result: MediaFailed after {sw.ElapsedMilliseconds} ms: {failure}"); player.Close(); return; }

        int vw = player.NaturalVideoWidth, vh = player.NaturalVideoHeight;
        line($"Opened in {sw.ElapsedMilliseconds} ms. Natural size {vw}x{vh}; duration {(player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds.ToString("0.0") + " s" : "unknown")}; HasVideo={player.HasVideo} HasAudio={player.HasAudio}");
        if (vw == 0 || vh == 0) { line("Result: no video stream reported"); player.Close(); return; }

        // ---- capture at the media's own size (1:1) and at a 1920x1080 canvas with the video placed in it ----
        foreach (var mode in new[] { "native", "canvas1080" })
        {
            var (cw, ch, ox, oy) = mode == "native" ? (vw, vh, 0, 0) : (1920, 1080, 200, 40);
            player.Position = TimeSpan.Zero;
            player.Play();
            Pump(400);

            var cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
            var wall = Stopwatch.StartNew();
            var hashes = new HashSet<int>();
            var costs = new List<double>();
            byte[]? lastPixels = null;
            var captures = 0;
            var maxMs = 5000;
            while (wall.ElapsedMilliseconds < maxMs)
            {
                var t = Stopwatch.StartNew();
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, cw, ch));
                    dc.DrawVideo(player, new Rect(ox, oy, vw, vh));
                }
                var rtb = new RenderTargetBitmap(cw, ch, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var px = new byte[cw * ch * 4];
                rtb.CopyPixels(px, cw * 4, 0);
                t.Stop();
                costs.Add(t.Elapsed.TotalMilliseconds);
                captures++;
                hashes.Add(Hash(px));
                lastPixels = px;
                Pump(10);
            }
            var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds;
            var seconds = wall.Elapsed.TotalSeconds;
            costs.Sort();
            line($"[{mode} {cw}x{ch}] {captures} captures in {seconds:0.0} s; distinct pictures {hashes.Count} (~{hashes.Count / seconds:0.0}/s); " +
                 $"grab cost median {costs[costs.Count / 2]:0.0} ms, p95 {costs[(int)(costs.Count * 0.95)]:0.0} ms, max {costs[^1]:0.0} ms; " +
                 $"process CPU {cpu / (seconds * 1000) * 100:0}% of one core; position now {player.Position.TotalSeconds:0.00} s");
            player.Pause();
            if (mode == "native" && lastPixels != null)
                File.WriteAllBytes(Path.ChangeExtension(path, ".captured.bgra"), lastPixels);
        }

        // ---- looping: seek to near the end and let it run off the end ----
        if (player.NaturalDuration.HasTimeSpan)
        {
            double? endedAt = null;
            var t0 = Stopwatch.StartNew();
            player.MediaEnded += (_, _) => endedAt ??= t0.Elapsed.TotalMilliseconds;
            player.Position = player.NaturalDuration.TimeSpan - TimeSpan.FromSeconds(1);
            player.Play();
            PumpUntil(() => endedAt != null, 4000);
            line(endedAt != null ? $"MediaEnded fired {endedAt:0} ms after seeking to 1 s before the end" : "MediaEnded did NOT fire within 4 s");
            var sw2 = Stopwatch.StartNew();
            player.Position = TimeSpan.Zero;
            player.Play();
            Pump(300);
            line($"Restart after the end (loop): position {player.Position.TotalSeconds:0.00} s after 300 ms; seek+play took {sw2.ElapsedMilliseconds} ms incl. wait");
        }
        player.Close();
    }

    private static void Pump(int ms)
    {
        var until = Stopwatch.StartNew();
        while (until.ElapsedMilliseconds < ms) { DoEvents(); Thread.Sleep(1); }
    }

    private static void PumpUntil(Func<bool> condition, int timeoutMs)
    {
        var until = Stopwatch.StartNew();
        while (!condition() && until.ElapsedMilliseconds < timeoutMs) { DoEvents(); Thread.Sleep(1); }
    }

    private static int Hash(byte[] b)
    {
        var h = new HashCode();
        for (var i = 0; i < b.Length; i += 97) h.Add(b[i]);
        return h.ToHashCode();
    }
}
