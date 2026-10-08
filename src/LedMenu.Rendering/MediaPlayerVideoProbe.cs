using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using LedMenu.Core.Assets;

namespace LedMenu.Rendering;

/// <summary>
/// Finds out whether Windows can play a video by opening it with the same decoder that will play it (WPF MediaPlayer,
/// Media Foundation), and reads its picture size and length (sound is ignored: the project has no audio). The player is created and driven on the
/// dispatcher it is given (the UI thread), whichever thread calls <see cref="ProbeAsync"/>. It never hangs: a file that
/// neither opens nor fails within the timeout is reported as failed.
/// </summary>
public sealed class MediaPlayerVideoProbe : IVideoProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly Dispatcher _dispatcher;

    public MediaPlayerVideoProbe(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public Task<VideoProbeResult> ProbeAsync(string path, CancellationToken cancel = default)
    {
        var done = new TaskCompletionSource<VideoProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancel.IsCancellationRequested) { done.SetCanceled(cancel); return done.Task; }
        if (!File.Exists(path)) { done.SetResult(VideoProbeResult.Failure("the file is missing")); return done.Task; }

        _dispatcher.BeginInvoke(new Action(() => Start(path, cancel, done)));
        return done.Task;
    }

    private void Start(string path, CancellationToken cancel, TaskCompletionSource<VideoProbeResult> done)
    {
        var player = new MediaPlayer { IsMuted = true, Volume = 0 };
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = Timeout };
        CancellationTokenRegistration registration = default;

        void Finish(VideoProbeResult? result)
        {
            if (done.Task.IsCompleted) return;
            timer.Stop();
            registration.Dispose();
            try { player.Close(); } catch { /* the probe must never throw */ }
            if (result == null) done.TrySetCanceled(cancel); else done.TrySetResult(result);
        }

        player.MediaOpened += (_, _) =>
        {
            int w = player.NaturalVideoWidth, h = player.NaturalVideoHeight;
            var seconds = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0;
            Finish(player.HasVideo && w > 0 && h > 0
                ? new VideoProbeResult(true, w, h, seconds, null)
                : VideoProbeResult.Failure("it has no picture Windows can show (an audio-only file, or a video format Windows lacks)"));
        };
        player.MediaFailed += (_, e) =>
        {
            // Windows' own text for a bad file is "Cannot find the media file", which is misleading, so it is not repeated.
            var detail = e.ErrorException?.Message;
            var useful = !string.IsNullOrWhiteSpace(detail) && !detail.Contains("Cannot find", StringComparison.OrdinalIgnoreCase);
            Finish(VideoProbeResult.Failure("Windows could not open it (a damaged file, or a video format this PC cannot decode)" + (useful ? ": " + detail : "")));
        };
        timer.Tick += (_, _) => Finish(VideoProbeResult.Failure("Windows took too long to open it"));

        if (cancel.CanBeCanceled)
            registration = cancel.Register(() => _dispatcher.BeginInvoke(new Action(() => Finish(null))));

        timer.Start();
        try { player.Open(new Uri(path)); }
        catch (Exception ex) { Finish(VideoProbeResult.Failure(ex.Message)); }
    }
}
