using System.Text;

namespace LedMenu.Core.Assets;

public enum VideoContainer { Mp4, Asf, Avi, Matroska }

/// <summary>What opening a video with the Windows decoder revealed.</summary>
public sealed record VideoProbeResult(bool Ok, int Width, int Height, double DurationSeconds, bool HasAudio, string? Problem)
{
    public static VideoProbeResult Failure(string problem) => new(false, 0, 0, 0, false, problem);
}

/// <summary>Opens a video file to learn its size and length, or to find out that it cannot be played. Implemented on top of Windows' own decoder.</summary>
public interface IVideoProbe
{
    Task<VideoProbeResult> ProbeAsync(string path, CancellationToken cancel = default);
}

/// <summary>
/// Rules for imported videos. Like images, the type is decided from the file's content, not its name, and videos are
/// referenced by plain file name only. Whether Windows can actually decode the file is decided later by <see cref="IVideoProbe"/>.
/// </summary>
public static class MediaRules
{
    public const long MaxBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>Containers recognised by content. H.264 in MP4 is the supported combination; the others are best effort.</summary>
    public static VideoContainer? Detect(ReadOnlySpan<byte> head)
    {
        // MP4 / MOV / M4V: a box whose type "ftyp" sits at offset 4 (QuickTime "moov"/"wide"/"mdat" first is also allowed)
        if (head.Length >= 12 && head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p') return VideoContainer.Mp4;
        if (head.Length >= 12 && ((head[4] == 'm' && head[5] == 'o' && head[6] == 'o' && head[7] == 'v') ||
                                  (head[4] == 'w' && head[5] == 'i' && head[6] == 'd' && head[7] == 'e') ||
                                  (head[4] == 'm' && head[5] == 'd' && head[6] == 'a' && head[7] == 't'))) return VideoContainer.Mp4;
        // ASF / WMV: 30 26 B2 75 8E 66 CF 11
        if (head.Length >= 8 && head[0] == 0x30 && head[1] == 0x26 && head[2] == 0xB2 && head[3] == 0x75 &&
            head[4] == 0x8E && head[5] == 0x66 && head[6] == 0xCF && head[7] == 0x11) return VideoContainer.Asf;
        // AVI: RIFF....AVI
        if (head.Length >= 12 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' &&
            head[8] == 'A' && head[9] == 'V' && head[10] == 'I' && head[11] == ' ') return VideoContainer.Avi;
        // Matroska / WebM: EBML header 1A 45 DF A3
        if (head.Length >= 4 && head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3) return VideoContainer.Matroska;
        return null;
    }

    public static string Extension(VideoContainer kind) => kind switch
    {
        VideoContainer.Mp4 => ".mp4",
        VideoContainer.Asf => ".wmv",
        VideoContainer.Avi => ".avi",
        _ => ".mkv",
    };

    public static bool IsSafeFileName(string? name) => AssetRules.IsSafeFileName(name);

    /// <summary>"{first 12 hex of content hash}-{clean original name}{extension}". Same content always gives the same name.</summary>
    public static string BuildFileName(string contentHashHex, string? originalBaseName, VideoContainer kind) =>
        $"{contentHashHex[..Math.Min(12, contentHashHex.Length)].ToLowerInvariant()}-{AssetRules.SanitizeBaseName(originalBaseName).Replace("image", "video", StringComparison.Ordinal)}{Extension(kind)}";

    /// <summary>Human text for a duration such as 95 seconds: "1:35".</summary>
    public static string FormatDuration(double seconds)
    {
        if (seconds <= 0) return "?";
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
