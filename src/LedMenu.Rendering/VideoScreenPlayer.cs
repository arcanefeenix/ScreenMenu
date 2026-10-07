using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LedMenu.Core.Calibration;
using LedMenu.Core.Logging;
using LedMenu.Core.Screens;
using LedMenu.Core.Video;

namespace LedMenu.Rendering;

public enum VideoPlayerState
{
    /// <summary>Not started (or stopped).</summary>
    Idle,
    /// <summary>A video is being opened.</summary>
    Opening,
    Playing,
    Paused,
    /// <summary>The last video ended and the playlist does not loop; the screen is black.</summary>
    Finished,
    /// <summary>Nothing can play (empty, all switched off, or every video failed); the screen is black.</summary>
    NothingPlayable,
}

/// <summary>
/// Plays one screen's playlist with the built-in Windows decoder and keeps the latest picture of that screen as a pixel buffer
/// of exactly the screen's size, ready to be copied into the output frame. Everything happens on the dispatcher thread it was created on.
///
/// - Two decoders are used. While one plays, the next video is already opened (paused at its start) on the other, so a change of
///   video or a loop does not stall. That is what removes the ~0.3 s restart gap measured in the feasibility test.
/// - Only this screen's rectangle is drawn, at most 30 times a second, and only while playing.
/// - Video pictures get the levels stretch (see <see cref="VideoLevels"/>); a video exactly the screen's size is drawn 1:1.
/// - A video that cannot be opened, fails while playing, or stops advancing is skipped and reported; it never stops the others
///   and it never throws into the application.
/// </summary>
public sealed class VideoScreenPlayer : IDisposable
{
    // Windows timers tick every 15.6 ms, so 33 ms would be delivered as 47 ms (21 pictures a second). 31 ms gives about 32, enough for 30 fps video.
    private static readonly TimeSpan GrabInterval = TimeSpan.FromMilliseconds(31);
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(10);
    private const int StallSeconds = 5;
    private static readonly TimeSpan FirstPictureMinPosition = TimeSpan.FromMilliseconds(80);   // a decoder's first couple of pictures can be blank or garbage
    private const int FirstPictureGraceMs = 1500;   // a slow decoder under load can take this long to deliver its first picture

    private sealed class Slot
    {
        public required MediaPlayer Player { get; init; }
        public required VideoItem Item { get; init; }
        public bool Opened { get; set; }
        public bool Failed { get; set; }
        public string? FailReason { get; set; }
        public bool Closed { get; set; }
        /// <summary>True once the decoder has moved past the start, i.e. it has a picture to draw.</summary>
        public bool HasPicture { get; set; }
        public Stopwatch SincePlay { get; } = new();
        public TimeSpan LastGrabPosition { get; set; } = TimeSpan.MinValue;
        public int Width { get; set; }
        public int Height { get; set; }
    }

    private readonly Dispatcher _dispatcher;
    private readonly int _width;
    private readonly int _height;
    private readonly Func<string, string?> _resolve;
    private readonly IAppLog _log;
    private readonly PlaylistSequencer _sequencer = new();
    private readonly DispatcherTimer _grabTimer;
    private readonly DispatcherTimer _watchTimer;
    private readonly PixelBuffer _frame;
    private RenderTargetBitmap? _target;
    private readonly DrawingVisual _visual = new();    // reused for every picture: no new drawing objects 30 times a second
    private byte[]? _scratch;

    private VideoPlaylist _playlist = new();
    private Slot? _active;
    private Slot? _standby;
    private bool _paused;
    private bool _started;
    private bool _disposed;
    private readonly Stopwatch _stateClock = Stopwatch.StartNew();
    private TimeSpan _lastPosition;
    private int _stalledTicks;

    public VideoScreenPlayer(Dispatcher dispatcher, int width, int height, Func<string, string?> resolveMediaPath, IAppLog? log = null)
    {
        _dispatcher = dispatcher;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _resolve = resolveMediaPath;
        _log = log ?? NullLog.Instance;
        _frame = new PixelBuffer(_width, _height);

        _grabTimer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = GrabInterval };
        _grabTimer.Tick += (_, _) => Safely(Grab);
        _watchTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _watchTimer.Tick += (_, _) => Safely(Watch);
    }

    // ---- what the rest of the program can see ----

    public int Width => _width;
    public int Height => _height;
    public VideoPlayerState State { get; private set; } = VideoPlayerState.Idle;

    /// <summary>The video that is playing (or paused) now.</summary>
    public VideoItem? Current => _active?.Item;

    /// <summary>The newest reason a video was skipped, for the operator. Cleared when playback starts again.</summary>
    public string? Problem { get; private set; }

    /// <summary>The screen's latest picture: exactly the screen's size, black when nothing is showing. Read it on the dispatcher thread.</summary>
    public PixelBuffer Frame => _frame;

    /// <summary>How many pictures have been grabbed since the player was created.</summary>
    public long FramesGrabbed { get; private set; }

    /// <summary>How many times the playing video changed (next video, loop, skip).</summary>
    public int ItemChanges { get; private set; }

    public bool Muted => _playlist.Muted;

    public event Action? FrameUpdated;
    public event Action? StateChanged;

    // ---- control ----

    /// <summary>Gives the player a playlist (or an edited one). Changes apply live: the video playing is only interrupted if it was removed or switched off.</summary>
    public void SetPlaylist(VideoPlaylist playlist) => Safely(() =>
    {
        _playlist = playlist;
        ApplyMute(_active);
        ApplyMute(_standby);
        var changed = _sequencer.SetItems(playlist.Items, playlist.Loop);
        if (!_started) return;
        if (changed) BeginItem(_sequencer.Current);
        else PrepareStandby();
    });

    /// <summary>Starts from the first playable video, or resumes after a pause.</summary>
    public void Play() => Safely(() =>
    {
        if (_started && State == VideoPlayerState.Paused) { Resume(); return; }
        _started = true;
        _paused = false;
        _sequencer.ClearFailures();
        Problem = null;
        _sequencer.SetItems(_playlist.Items, _playlist.Loop);
        BeginItem(_sequencer.Start());
    });

    public void Pause() => Safely(() =>
    {
        if (!_started || State is not (VideoPlayerState.Playing or VideoPlayerState.Opening)) return;
        _paused = true;
        try { _active?.Player.Pause(); } catch { }
        SetState(VideoPlayerState.Paused);
    });

    public void Next() => Safely(() => { if (_started) BeginItem(_sequencer.Next()); });

    public void Previous() => Safely(() => { if (_started) BeginItem(_sequencer.Previous()); });

    /// <summary>Stops everything and blanks the screen.</summary>
    public void Stop() => Safely(() =>
    {
        _started = false;
        _paused = false;
        CloseSlot(ref _active);
        CloseSlot(ref _standby);
        StopTimers();
        Blank();
        SetState(VideoPlayerState.Idle);
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Safely(() => { CloseSlot(ref _active); CloseSlot(ref _standby); StopTimers(); });
    }

    // ---- moving between videos ----

    private void BeginItem(VideoItem? item)
    {
        if (_disposed) return;
        if (item == null)
        {
            CloseSlot(ref _active);
            CloseSlot(ref _standby);
            StopTimers();
            if (_sequencer.Status == SequencerStatus.NothingPlayable && Problem == null)
                Problem = _playlist.Items.Count == 0 ? "There are no videos on this screen." : "No video on this screen can be played.";
            SetState(_sequencer.Status == SequencerStatus.Finished ? VideoPlayerState.Finished : VideoPlayerState.NothingPlayable);
            Blank();                       // state first, so anyone told about the new picture already sees that nothing is playing
            return;
        }

        ItemChanges++;
        Slot? next = null;
        if (_standby != null && _standby.Item.Id == item.Id && !_standby.Closed)
        {
            // the video was already opened in the background: change over at once
            next = _standby;
            _standby = null;
        }
        CloseSlot(ref _active);

        next ??= OpenSlot(item);
        if (next == null) { FailCurrent("its file is missing from the media folder"); return; }
        _active = next;
        _lastPosition = TimeSpan.MinValue;
        _stalledTicks = 0;
        _stateClock.Restart();
        ApplyMute(_active);

        if (_active.Failed) { FailCurrent(_active.FailReason ?? "it could not be opened"); return; }

        if (_active.Opened) StartPlaying(_active);
        else SetState(VideoPlayerState.Opening);
        EnsureTimers();
    }

    private void StartPlaying(Slot slot)
    {
        if (_paused) { SetState(VideoPlayerState.Paused); return; }
        try { slot.Player.Play(); slot.SincePlay.Restart(); }
        catch (Exception ex) { FailCurrent("it could not be started: " + ex.Message); return; }
        SetState(VideoPlayerState.Playing);
        PrepareStandby();
    }

    private void Resume()
    {
        _paused = false;
        if (_active is { Opened: true } a)
        {
            try { a.Player.Play(); } catch { }
            _lastPosition = TimeSpan.MinValue;
            _stalledTicks = 0;
            SetState(VideoPlayerState.Playing);
        }
        else SetState(VideoPlayerState.Opening);
    }

    private void FailCurrent(string reason)
    {
        var bad = _active?.Item ?? _sequencer.Current;
        var label = bad == null ? "A video" : $"\"{(string.IsNullOrWhiteSpace(bad.DisplayName) ? bad.FileName : bad.DisplayName)}\"";
        Problem = $"{label} was skipped: {reason}.";
        _log.Warn($"Video skipped: {label}: {reason}");
        CloseSlot(ref _active);
        BeginItem(_sequencer.Failed());
    }

    /// <summary>Opens whatever plays after the current video on the spare decoder, paused at its start.</summary>
    private void PrepareStandby()
    {
        if (_disposed || _active == null) return;
        var peek = _sequencer.PeekNext();
        if (peek == null) { CloseSlot(ref _standby); return; }
        if (_standby != null && _standby.Item.Id == peek.Id && !_standby.Closed) return;
        CloseSlot(ref _standby);
        _standby = OpenSlot(peek);
    }

    private Slot? OpenSlot(VideoItem item)
    {
        var path = _resolve(item.FileName);
        if (path == null) return null;

        var slot = new Slot { Player = new MediaPlayer { ScrubbingEnabled = true }, Item = item };   // scrubbing lets a paused spare decoder show its first picture
        ApplyMute(slot);
        slot.Player.MediaOpened += (_, _) => Safely(() => OnOpened(slot));
        slot.Player.MediaFailed += (_, e) => Safely(() => OnFailed(slot, e.ErrorException?.Message));
        slot.Player.MediaEnded += (_, _) => Safely(() => OnEnded(slot));
        try { slot.Player.Open(new Uri(path)); }
        catch (Exception ex) { slot.Failed = true; slot.FailReason = ex.Message; }
        return slot;
    }

    // ---- decoder events ----

    private void OnOpened(Slot slot)
    {
        if (slot.Closed || _disposed) return;
        slot.Opened = true;
        slot.Width = slot.Player.NaturalVideoWidth;
        slot.Height = slot.Player.NaturalVideoHeight;
        if (slot.Width <= 0 || slot.Height <= 0) { OnFailed(slot, "it has no picture"); return; }

        if (slot == _active) StartPlaying(slot);
        else { try { slot.Player.Pause(); } catch { } }          // the spare decoder waits at the first picture
    }

    private void OnFailed(Slot slot, string? detail)
    {
        if (slot.Closed || _disposed) return;
        slot.Failed = true;
        slot.FailReason = "Windows could not play it (a damaged file or an unsupported video format)" +
                          (!string.IsNullOrWhiteSpace(detail) && !detail.Contains("Cannot find", StringComparison.OrdinalIgnoreCase) ? ": " + detail : "");
        if (slot == _active) FailCurrent(slot.FailReason);
        // a spare that fails is found out (and skipped) when its turn comes
    }

    private void OnEnded(Slot slot)
    {
        if (slot.Closed || _disposed || slot != _active) return;
        BeginItem(_sequencer.Ended());
    }

    // ---- timers ----

    private void EnsureTimers()
    {
        if (!_grabTimer.IsEnabled) _grabTimer.Start();
        if (!_watchTimer.IsEnabled) _watchTimer.Start();
    }

    private void StopTimers()
    {
        _grabTimer.Stop();
        _watchTimer.Stop();
    }

    /// <summary>Once a second: a video that never opens, or stops advancing, is skipped instead of freezing the screen forever.</summary>
    private void Watch()
    {
        if (_active == null) return;
        if (State == VideoPlayerState.Opening && !_paused && _stateClock.Elapsed > OpenTimeout)
        {
            FailCurrent("Windows took too long to open it");
            return;
        }
        if (State != VideoPlayerState.Playing) return;

        var position = _active.Player.Position;
        if (position == _lastPosition)
        {
            if (++_stalledTicks >= StallSeconds) FailCurrent("it stopped playing (stalled)");
        }
        else { _lastPosition = position; _stalledTicks = 0; }
    }

    // ---- the picture ----

    private void Grab()
    {
        if (_active is not { Opened: true } slot || State != VideoPlayerState.Playing) return;

        // A decoder that has only just started has nothing to draw yet. Until it has, the previous picture stays up,
        // so changing videos or looping never flashes black.
        if (!slot.HasPicture && slot.Player.Position < FirstPictureMinPosition) return;

        var dest = VideoPlacement.Compute(slot.Width, slot.Height, _width, _height, _playlist.Fit);
        var native = VideoPlacement.IsNative(dest, slot.Width, slot.Height);

        var visual = _visual;
        RenderOptions.SetBitmapScalingMode(visual, native ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            var whole = new Rect(0, 0, _width, _height);
            dc.DrawRectangle(Brushes.Black, null, whole);
            dc.PushClip(new RectangleGeometry(whole));        // strictly inside this screen, however the picture is placed
            dc.DrawVideo(slot.Player, new Rect(dest.X, dest.Y, dest.Width, dest.Height));
            dc.Pop();
        }

        _target ??= new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
        _target.Clear();
        _target.Render(visual);
        _scratch ??= new byte[_frame.Data.Length];
        _target.CopyPixels(_scratch, _width * 4, 0);

        // The decoder can report progress a moment before it has a picture; an all-black grab in a new video's first second and a half
        // is that gap, not content, so the previous picture stays up (a video that really starts black shows up to 1.5 seconds late).
        if (!slot.HasPicture)
        {
            if (IsBlack(_scratch) && slot.SincePlay.ElapsedMilliseconds < FirstPictureGraceMs) return;
            slot.HasPicture = true;
        }

        // At the very end of a clip the decoder's clock stops and the renderer can blank before the end event arrives.
        // Real black content always has an advancing clock, so a black picture on a standing clock is that blip: keep the last picture.
        var position = slot.Player.Position;
        var clockStopped = position == slot.LastGrabPosition;
        slot.LastGrabPosition = position;
        if (clockStopped && IsBlack(_scratch)) return;

        Buffer.BlockCopy(_scratch, 0, _frame.Data, 0, _scratch.Length);
        VideoLevels.ExpandLimitedRange(_frame.Data);
        FramesGrabbed++;
        FrameUpdated?.Invoke();
    }

    /// <summary>True if every sampled pixel is pure black (alpha ignored). Sampling keeps this cheap.</summary>
    private static bool IsBlack(byte[] bgra)
    {
        for (var i = 0; i + 2 < bgra.Length; i += 4 * 7)
            if (bgra[i] != 0 || bgra[i + 1] != 0 || bgra[i + 2] != 0) return false;
        return true;
    }

    private void Blank()
    {
        var d = _frame.Data;
        for (var i = 0; i + 3 < d.Length; i += 4) { d[i] = 0; d[i + 1] = 0; d[i + 2] = 0; d[i + 3] = 255; }
        FrameUpdated?.Invoke();
    }

    // ---- helpers ----

    private void ApplyMute(Slot? slot)
    {
        if (slot == null) return;
        try { slot.Player.IsMuted = _playlist.Muted; slot.Player.Volume = _playlist.Muted ? 0 : 1; } catch { }
    }

    private void SetState(VideoPlayerState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke();
    }

    private static void CloseSlot(ref Slot? slot)
    {
        if (slot == null) return;
        slot.Closed = true;
        try { slot.Player.Close(); } catch { }
        slot = null;
    }

    /// <summary>Nothing a decoder does may throw into the application.</summary>
    private void Safely(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            Problem = "The video player hit an error: " + ex.Message;
            // the same error repeating every picture must not flood the log
            if (ex.Message != _lastError || _errorClock.Elapsed > TimeSpan.FromSeconds(10))
            {
                _lastError = ex.Message;
                _errorClock.Restart();
                _log.Error("Video player error (the screen keeps running).", ex);
            }
        }
    }

    private string? _lastError;
    private readonly Stopwatch _errorClock = Stopwatch.StartNew();
}
