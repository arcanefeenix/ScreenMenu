using LedMenu.Core.Display;

namespace LedMenu.Core.Screens;

public enum CanvasSource
{
    /// <summary>The output display is connected and confirmed right now.</summary>
    Live,
    /// <summary>The output display is not available; size comes from what was remembered.</summary>
    LastKnown,
    /// <summary>No output display has ever been selected.</summary>
    Unknown,
}

public sealed record CanvasInfo(CanvasSize? Size, CanvasSource Source, string? DisplayName);

/// <summary>Works out which pixel canvas screens should be validated against. Never changes the saved display.</summary>
public static class OutputCanvasResolver
{
    public static CanvasInfo Resolve(MatchResult output, DisplayIdentity? saved)
    {
        if (output.Kind == MatchKind.Exact && output.Display is { Width: > 0, Height: > 0 } d)
            return new(new CanvasSize(d.Width, d.Height), CanvasSource.Live, d.FriendlyName);

        if (saved is { Width: > 0, Height: > 0 })
            return new(new CanvasSize(saved.Width, saved.Height), CanvasSource.LastKnown, saved.FriendlyName);

        return new(null, CanvasSource.Unknown, null);
    }
}

public static class ScreenPlacement
{
    /// <summary>
    /// First free spot for a new screen: tries the top-left, then positions just right of and just below
    /// existing enabled screens. Falls back to (0,0) when nothing fits (the validator will then flag it).
    /// </summary>
    public static (int X, int Y) NextFreePosition(IReadOnlyList<Screen> existing, CanvasSize? canvas, int width, int height)
    {
        var live = existing.Where(s => s.Enabled && s.Width > 0 && s.Height > 0).ToList();
        var xs = new SortedSet<int> { 0 };
        var ys = new SortedSet<int> { 0 };
        foreach (var s in live)
        {
            if (s.X + (long)s.Width < int.MaxValue) xs.Add(s.X + s.Width);
            if (s.Y + (long)s.Height < int.MaxValue) ys.Add(s.Y + s.Height);
        }

        foreach (var y in ys)
            foreach (var x in xs)
            {
                if (x < 0 || y < 0) continue;
                if (canvas is { } c && ((long)x + width > c.Width || (long)y + height > c.Height)) continue;
                var candidate = new Screen { X = x, Y = y, Width = width, Height = height };
                if (live.All(s => !ScreenValidator.Overlaps(s, candidate))) return (x, y);
            }
        return (0, 0);
    }
}

public static class SnapHelper
{
    /// <summary>Returns the nearest target within threshold of <paramref name="value"/>, else value unchanged.</summary>
    public static int Snap(int value, IEnumerable<int> targets, int threshold)
    {
        var best = value;
        var bestDistance = threshold + 1;
        foreach (var t in targets)
        {
            var d = Math.Abs(t - value);
            if (d <= threshold && d < bestDistance) { best = t; bestDistance = d; }
        }
        return best;
    }

    /// <summary>
    /// Snaps a moving span by its start or its end, whichever is closer to a target.
    /// Returns the (possibly adjusted) start; the length never changes.
    /// </summary>
    public static int SnapSpanStart(int start, int length, IReadOnlyCollection<int> targets, int threshold)
    {
        var snappedStart = Snap(start, targets, threshold);
        var snappedEnd = Snap(start + length, targets, threshold) - length;
        var startMoved = Math.Abs(snappedStart - start);
        var endMoved = Math.Abs(snappedEnd - start);
        if (snappedStart == start && snappedEnd == start) return start;
        if (snappedStart == start) return snappedEnd;
        if (snappedEnd == start) return snappedStart;
        return startMoved <= endMoved ? snappedStart : snappedEnd;
    }
}
