using System.Text;

namespace LedMenu.Core.Assets;

public enum ImageKind { Png, Jpeg, Bmp, Gif }

/// <summary>
/// Rules for imported image assets. Menus reference an asset by plain file name only, so the name is checked
/// to be a bare file name (no folders, no ".."), and the file type is decided from its content, not its extension.
/// </summary>
public static class AssetRules
{
    public const long MaxBytes = 20L * 1024 * 1024;
    public const int MaxBaseNameLength = 48;

    private static readonly char[] InvalidNameChars =
        { '/', (char)92, ':', '*', '?', (char)34, '<', '>', '|', (char)0 };   // 92 = backslash, 34 = double quote

    public static ImageKind? Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 &&
            head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A) return ImageKind.Png;
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ImageKind.Jpeg;
        if (head.Length >= 2 && head[0] == 0x42 && head[1] == 0x4D) return ImageKind.Bmp;
        if (head.Length >= 6 && head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8' &&
            (head[4] == '7' || head[4] == '9') && head[5] == 'a') return ImageKind.Gif;
        return null;
    }

    public static string Extension(ImageKind kind) => kind switch
    {
        ImageKind.Png => ".png",
        ImageKind.Jpeg => ".jpg",
        ImageKind.Bmp => ".bmp",
        _ => ".gif",
    };

    /// <summary>True for a plain file name that cannot point outside the assets folder.</summary>
    public static bool IsSafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) return false;
        if (name != name.Trim()) return false;
        if (name.Contains("..") || name.IndexOfAny(InvalidNameChars) >= 0) return false;
        return !name.Any(char.IsControl);
    }

    /// <summary>Keeps letters, digits, '-' and '_'; everything else becomes '_'. Never empty.</summary>
    public static string SanitizeBaseName(string? name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name ?? "")
        {
            if (sb.Length >= MaxBaseNameLength) break;
            sb.Append(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_');
        }
        var result = sb.ToString().Trim('_');
        return result.Length == 0 ? "image" : result;
    }

    /// <summary>"{first 12 hex of content hash}-{clean original name}{extension}". Same content always gives the same name.</summary>
    public static string BuildFileName(string contentHashHex, string? originalBaseName, ImageKind kind) =>
        $"{contentHashHex[..Math.Min(12, contentHashHex.Length)].ToLowerInvariant()}-{SanitizeBaseName(originalBaseName)}{Extension(kind)}";
}
