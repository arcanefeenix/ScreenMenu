using System.Text.Json.Serialization;

namespace LedMenu.Core.Screens;

/// <summary>What a screen shows. A screen keeps its menu assignment when switched to Video, so switching back loses nothing.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScreenContentKind
{
    Menu = 0,
    Video = 1,
}

/// <summary>How a video whose shape differs from its screen is placed. A video exactly the screen's size is always shown 1:1 whatever this says.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VideoFit
{
    /// <summary>The whole picture is visible, scaled to fit inside the screen; leftover space is black.</summary>
    Fit = 0,
    /// <summary>The picture covers the whole screen; whatever sticks out is cropped.</summary>
    Fill = 1,
}

/// <summary>One imported video in a playlist. The file itself lives in the app's media folder and is referenced by plain file name.</summary>
public sealed class VideoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Name of the copy in the media folder (never a path).</summary>
    public string FileName { get; set; } = "";

    /// <summary>What the operator sees: the original file's name.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>A disabled video stays in the playlist but is skipped.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Picture size recorded at import. Zero if unknown.</summary>
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Length in seconds recorded at import; zero if unknown.</summary>
    public double DurationSeconds { get; set; }

    public VideoItem Clone() => (VideoItem)MemberwiseClone();
}

/// <summary>An ordered list of videos shown one after another on a screen. There is no sound: video is always played muted and audio tracks are ignored.</summary>
public sealed class VideoPlaylist
{
    public List<VideoItem> Items { get; set; } = new();

    /// <summary>Start again from the first video after the last one ends.</summary>
    public bool Loop { get; set; } = true;

    public VideoFit Fit { get; set; } = VideoFit.Fit;

    /// <summary>Videos that will actually play, in order.</summary>
    [JsonIgnore]
    public IEnumerable<VideoItem> Playable => Items.Where(i => i.Enabled);

    public VideoPlaylist Clone() => new()
    {
        Items = Items.Select(i => i.Clone()).ToList(),
        Loop = Loop,
        Fit = Fit,
    };
}
