using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using LedMenu.Core.Screens;
using LedMenu.Rendering;

// Usage: VideoSoak <seconds> <reportFile> <clip1> [clip2 ...]
// Plays the clips as a looping playlist on a 168x672 screen with the real player and reports smoothness, transitions, CPU and memory.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var seconds = int.Parse(args[0]);
        var reportPath = args[1];
        var clips = args.Skip(2).ToArray();
        var report = new List<string>();
        void Line(string s) { report.Add(s); Console.WriteLine(s); }

        var dispatcher = Dispatcher.CurrentDispatcher;
        var playlist = new VideoPlaylist { Loop = true, Muted = true, Fit = VideoFit.Fit };
        var byName = new Dictionary<string, string>();
        foreach (var c in clips)
        {
            var name = Path.GetFileName(c);
            byName[name] = c;
            playlist.Items.Add(new VideoItem { FileName = name, DisplayName = name, Width = 168, Height = 672 });
        }

        using var player = new VideoScreenPlayer(dispatcher, 168, 672, n => byName.GetValueOrDefault(n));
        var gaps = new List<double>();
        var clock = Stopwatch.StartNew();
        var last = 0.0;
        var blackAfterFirst = 0; var seenPicture = false;
        var changeTimes = new List<double>();
        var lastItem = (Guid?)null;
        player.FrameUpdated += () =>
        {
            var now = clock.Elapsed.TotalMilliseconds;
            if (last > 0) gaps.Add(now - last);
            last = now;
            var black = true;
            var d = player.Frame.Data;
            for (var i = 0; i < d.Length; i += 4 * 11) if (d[i] != 0 || d[i + 1] != 0 || d[i + 2] != 0) { black = false; break; }
            if (!black) seenPicture = true; else if (seenPicture && player.State == VideoPlayerState.Playing) blackAfterFirst++;
            if (player.Current?.Id != lastItem) { lastItem = player.Current?.Id; changeTimes.Add(now); }
        };

        var proc = Process.GetCurrentProcess();
        player.SetPlaylist(playlist);
        player.Play();
        Line($"VideoSoak {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {clips.Length} clip(s) looping for {seconds} s on a 168x672 screen");
        foreach (var c in clips) Line("  " + Path.GetFileName(c));

        var cpu0 = proc.TotalProcessorTime;
        var started = Stopwatch.StartNew();
        var memStart = 0L;
        var samples = new List<long>();
        var managed = new List<long>();
        var nextSample = 0;
        while (started.Elapsed.TotalSeconds < seconds)
        {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(2);
            if ((int)started.Elapsed.TotalSeconds >= nextSample)
            {
                proc.Refresh();
                samples.Add(proc.WorkingSet64);
                managed.Add(GC.GetTotalMemory(false));
                if (memStart == 0) memStart = proc.WorkingSet64;
                nextSample += 10;
            }
        }
        proc.Refresh();
        var cpu = (proc.TotalProcessorTime - cpu0).TotalSeconds / started.Elapsed.TotalSeconds * 100;
        gaps.Sort();
        var fps = player.FramesGrabbed / started.Elapsed.TotalSeconds;

        Line("");
        Line($"State at end: {player.State}; videos started: {player.ItemChanges}; pictures grabbed: {player.FramesGrabbed} ({fps:0.0}/s)");
        if (gaps.Count > 0)
            Line($"Gap between pictures: median {gaps[gaps.Count / 2]:0.0} ms, p99 {gaps[(int)(gaps.Count * 0.99)]:0.0} ms, longest {gaps[^1]:0.0} ms");
        Line($"Black pictures after the first real picture, while playing: {blackAfterFirst}");
        Line($"Process CPU: {cpu:0}% of one core");
        Line($"Memory (working set) every 10 s, MB: {string.Join(", ", samples.Select(s => s / 1024 / 1024))}");
        Line($"Managed heap (before collecting) every 10 s, MB: {string.Join(", ", managed.Select(s => s / 1024 / 1024))}");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        proc.Refresh();
        Line($"After a full garbage collection at the end: managed {GC.GetTotalMemory(true) / 1024 / 1024} MB, working set {proc.WorkingSet64 / 1024 / 1024} MB; collections gen0/1/2 = {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
        Line($"Memory growth over the run: {(samples[^1] - memStart) / 1024.0 / 1024.0:0.0} MB");
        Line($"Problems reported: {player.Problem ?? "(none)"}");
        File.WriteAllLines(reportPath, report);
        player.Stop();
        return 0;
    }
}
