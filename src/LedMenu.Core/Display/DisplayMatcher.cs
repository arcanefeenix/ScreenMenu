namespace LedMenu.Core.Display;

/// <summary>Supplies the currently connected displays. Win32 in the app; a fake in tests.</summary>
public interface IDisplaySource
{
    IReadOnlyList<DisplayInfo> GetDisplays();
}

public enum MatchKind
{
    /// <summary>No display was ever chosen.</summary>
    NotConfigured,
    /// <summary>Found by stable device path.</summary>
    Exact,
    /// <summary>Device path not found, but exactly one display has the same name, size and position. Needs operator confirmation.</summary>
    Fallback,
    /// <summary>The remembered display is not connected.</summary>
    NotFound,
}

public sealed record MatchResult(MatchKind Kind, DisplayInfo? Display)
{
    public bool IsAvailable => Kind is MatchKind.Exact or MatchKind.Fallback;
}

/// <summary>
/// Finds a remembered display among the connected ones. It never substitutes a different
/// display: if nothing credible matches, the result is NotFound.
/// </summary>
public static class DisplayMatcher
{
    public static MatchResult Match(DisplayIdentity? identity, IReadOnlyList<DisplayInfo> available)
    {
        if (identity is null) return new(MatchKind.NotConfigured, null);

        if (!string.IsNullOrWhiteSpace(identity.DevicePath))
        {
            var byPath = available
                .Where(d => string.Equals(d.DevicePath, identity.DevicePath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byPath.Count == 1) return new(MatchKind.Exact, byPath[0]);
        }

        // The device path changed or is unavailable (driver/cable/port change). Accept only a single
        // display that looks identical: same name, same resolution, same desktop position.
        var lookalikes = available
            .Where(d => string.Equals(d.FriendlyName, identity.FriendlyName, StringComparison.OrdinalIgnoreCase)
                        && d.Width == identity.Width && d.Height == identity.Height
                        && d.X == identity.X && d.Y == identity.Y)
            .ToList();
        if (lookalikes.Count == 1 && !string.IsNullOrWhiteSpace(identity.FriendlyName))
            return new(MatchKind.Fallback, lookalikes[0]);

        return new(MatchKind.NotFound, null);
    }

    /// <summary>Stable left-to-right, top-to-bottom order used for the on-screen labels 1, 2, 3...</summary>
    public static IReadOnlyList<DisplayInfo> Order(IEnumerable<DisplayInfo> displays) =>
        displays.OrderBy(d => d.X).ThenBy(d => d.Y).ThenBy(d => d.DevicePath, StringComparer.Ordinal).ToList();
}
