using System.IO;
using System.Windows.Threading;
using LedMenu.Core.Assets;
using LedMenu.Persistence;
using LedMenu.Rendering;

namespace LedMenu.App.Tests;

/// <summary>The real Windows decoder, on small clips generated for the tests (a 1-second 168x672 H.264 test pattern, with and without sound).</summary>
public class VideoProbeTests : IDisposable
{
    private static readonly string Media = Path.Combine(AppContext.BaseDirectory, "media");
    private readonly string _work = Path.Combine(Path.GetTempPath(), "ledmenu-probe-" + Guid.NewGuid().ToString("N"));

    public VideoProbeTests() => Directory.CreateDirectory(_work);
    public void Dispose() { try { Directory.Delete(_work, true); } catch { } }

    /// <summary>Runs <paramref name="body"/> on an STA thread and keeps the WPF dispatcher turning until its task finishes.</summary>
    private static T Run<T>(Func<Task<T>> body)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var task = body();
                var until = System.Diagnostics.Stopwatch.StartNew();
                while (!task.IsCompleted)
                {
                    var frame = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                    Thread.Sleep(1);
                    if (until.Elapsed > TimeSpan.FromSeconds(40)) throw new TimeoutException("the probe never finished");
                }
                result = task.GetAwaiter().GetResult();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }

    private string Copy(string name, int? firstBytes = null)
    {
        var dest = Path.Combine(_work, name);
        var bytes = File.ReadAllBytes(Path.Combine(Media, "tiny-168x672.mp4"));
        File.WriteAllBytes(dest, firstBytes is { } n ? bytes[..n] : bytes);
        return dest;
    }

    [Fact]
    public void A_good_clip_reports_its_size_and_length() => Run(async () =>
    {
        var r = await new MediaPlayerVideoProbe(Dispatcher.CurrentDispatcher).ProbeAsync(Path.Combine(Media, "tiny-168x672.mp4"));
        Assert.True(r.Ok, r.Problem);
        Assert.Equal((168, 672), (r.Width, r.Height));
        Assert.InRange(r.DurationSeconds, 0.9, 1.2);
        return 0;
    });

    [Fact]
    public void The_length_reader_agrees_with_the_real_files_it_will_meet()
    {
        Assert.InRange(LedMenu.Core.Assets.Mp4Info.TryReadDurationSeconds(Path.Combine(Media, "tiny-168x672.mp4")) ?? 0, 0.99, 1.01);
        Assert.InRange(LedMenu.Core.Assets.Mp4Info.TryReadDurationSeconds(Path.Combine(Media, "solid-red.mp4")) ?? 0, 0.99, 1.01);
        Assert.Null(LedMenu.Core.Assets.Mp4Info.TryReadDurationSeconds(Copy("cut-short.mp4", firstBytes: 3000)));     // damaged: no length, no crash
    }

    [Fact]
    public void A_clip_with_a_sound_track_is_accepted_like_any_other_the_sound_is_just_ignored() => Run(async () =>
    {
        var r = await new MediaPlayerVideoProbe(Dispatcher.CurrentDispatcher).ProbeAsync(Path.Combine(Media, "tiny-168x672-audio.mp4"));
        Assert.True(r.Ok, r.Problem);
        Assert.Equal((168, 672), (r.Width, r.Height));
        return 0;
    });

    [Fact]
    public void A_truncated_clip_fails_cleanly_with_a_reason() => Run(async () =>
    {
        var r = await new MediaPlayerVideoProbe(Dispatcher.CurrentDispatcher).ProbeAsync(Copy("cut.mp4", firstBytes: 3000));
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Problem));
        Assert.DoesNotContain("Cannot find the media file", r.Problem);      // the decoder's own message is misleading, so it is replaced
        return 0;
    });

    [Fact]
    public void Garbage_with_an_mp4_header_fails_cleanly() => Run(async () =>
    {
        var path = Path.Combine(_work, "junk.mp4");
        var bytes = new byte[5000];
        new Random(4).NextBytes(bytes);
        bytes[3] = 0x20; bytes[4] = 0x66; bytes[5] = 0x74; bytes[6] = 0x79; bytes[7] = 0x70;
        File.WriteAllBytes(path, bytes);
        var r = await new MediaPlayerVideoProbe(Dispatcher.CurrentDispatcher).ProbeAsync(path);
        Assert.False(r.Ok);
        return 0;
    });

    [Fact]
    public void A_missing_file_fails_without_throwing() => Run(async () =>
    {
        var r = await new MediaPlayerVideoProbe(Dispatcher.CurrentDispatcher).ProbeAsync(Path.Combine(_work, "nothing-here.mp4"));
        Assert.False(r.Ok);
        Assert.Contains("missing", r.Problem);
        return 0;
    });

    [Fact]
    public void A_cancelled_probe_ends_promptly_as_cancelled() => Run(async () =>
    {
        using var cts = new CancellationTokenSource();
        var task = new MediaPlayerVideoProbe(Dispatcher.CurrentDispatcher).ProbeAsync(Path.Combine(Media, "tiny-168x672.mp4"), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        return 0;
    });

    [Fact]
    public void Importing_a_real_clip_stores_a_playable_copy_with_its_facts() => Run(async () =>
    {
        var store = new MediaStore(Path.Combine(_work, "media"), new MediaPlayerVideoProbe(Dispatcher.CurrentDispatcher));
        var r = await store.ImportAsync(Path.Combine(Media, "tiny-168x672.mp4"));
        Assert.Equal((168, 672), (r.Item.Width, r.Item.Height));
        Assert.InRange(r.Item.DurationSeconds, 0.9, 1.2);
        Assert.NotNull(store.Resolve(r.Item.FileName));
        Assert.Equal("tiny-168x672.mp4", r.Item.DisplayName);

        var again = await store.ImportAsync(Path.Combine(Media, "tiny-168x672.mp4"));
        Assert.True(again.AlreadyPresent);
        return 0;
    });

    [Fact]
    public void Importing_a_damaged_clip_is_refused_and_leaves_the_media_folder_empty() => Run(async () =>
    {
        var store = new MediaStore(Path.Combine(_work, "media"), new MediaPlayerVideoProbe(Dispatcher.CurrentDispatcher));
        var ex = await Assert.ThrowsAsync<MediaImportException>(() => store.ImportAsync(Copy("damaged.mp4", firstBytes: 3000)));
        Assert.Contains("cannot play", ex.Message);
        Assert.Empty(Directory.GetFiles(Path.Combine(_work, "media")));
        return 0;
    });
}
