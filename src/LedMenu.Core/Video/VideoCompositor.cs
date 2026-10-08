using LedMenu.Core.Calibration;
using LedMenu.Core.Screens;

namespace LedMenu.Core.Video;

/// <summary>Rectangle arithmetic on whole pixels.</summary>
public static class RegionMath
{
    public static PixelRegion? Intersect(PixelRegion a, PixelRegion b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min((long)a.X + a.Width, (long)b.X + b.Width);
        var bottom = Math.Min((long)a.Y + a.Height, (long)b.Y + b.Height);
        if (right <= left || bottom <= top) return null;
        return new PixelRegion(left, top, (int)(right - left), (int)(bottom - top));
    }

    /// <summary>What is left of <paramref name="r"/> after removing <paramref name="cut"/>: up to four non-overlapping rectangles (empty if fully covered).</summary>
    public static IReadOnlyList<PixelRegion> Subtract(PixelRegion r, PixelRegion cut)
    {
        if (r.Width <= 0 || r.Height <= 0) return Array.Empty<PixelRegion>();
        if (Intersect(r, cut) is not { } hit) return new[] { r };

        var parts = new List<PixelRegion>(4);
        if (hit.Y > r.Y) parts.Add(new PixelRegion(r.X, r.Y, r.Width, hit.Y - r.Y));                                                    // above
        var hitBottom = hit.Y + hit.Height;
        if (hitBottom < r.Y + r.Height) parts.Add(new PixelRegion(r.X, hitBottom, r.Width, r.Y + r.Height - hitBottom));                 // below
        if (hit.X > r.X) parts.Add(new PixelRegion(r.X, hit.Y, hit.X - r.X, hit.Height));                                             // left of the cut
        var hitRight = hit.X + hit.Width;
        if (hitRight < r.X + r.Width) parts.Add(new PixelRegion(hitRight, hit.Y, r.X + r.Width - hitRight, hit.Height));                // right of the cut
        return parts;
    }

    /// <summary>The parts of <paramref name="region"/> not covered by any of <paramref name="covers"/>.</summary>
    public static IReadOnlyList<PixelRegion> VisibleParts(PixelRegion region, IEnumerable<PixelRegion> covers)
    {
        if (region.Width <= 0 || region.Height <= 0) return Array.Empty<PixelRegion>();
        IReadOnlyList<PixelRegion> pieces = new[] { region };
        foreach (var cover in covers)
        {
            if (pieces.Count == 0) break;
            var next = new List<PixelRegion>();
            foreach (var p in pieces) next.AddRange(Subtract(p, cover));
            pieces = next;
        }
        return pieces;
    }
}

/// <summary>
/// Puts each video screen's picture into the shared output picture. A video can only ever change pixels inside its own screen
/// rectangle, and only where no screen drawn after it (later in the list, so on top) covers it, so a video can never
/// disturb a neighbouring screen or paint over one that sits above it.
/// </summary>
public static class VideoCompositor
{
    /// <summary>
    /// Builds the whole output picture: the menus' picture (or black if there is none) with every video screen's current picture put in.
    /// The returned buffer is always <paramref name="canvas"/> sized. A fresh menu picture is reused as it is (it is the compositor's to change).
    /// </summary>
    public static PixelBuffer? Compose(PixelBuffer? menuPicture, CanvasSize canvas, IReadOnlyList<CalibrationScreen> drawn, Func<Guid, PixelBuffer?> videoPicture)
    {
        var videos = drawn.Where(s => s.IsVideo).ToList();
        if (videos.Count == 0) return menuPicture;

        var target = menuPicture != null && menuPicture.Width == canvas.Width && menuPicture.Height == canvas.Height
            ? menuPicture
            : new PixelBuffer(canvas.Width, canvas.Height);
        foreach (var s in videos)
            if (videoPicture(s.Id) is { } v) Blit(target, s, v, drawn);
        return target;
    }

    /// <summary>
    /// Copies <paramref name="video"/> (which must be exactly the screen's size) into the visible parts of the screen's rectangle.
    /// Returns the rectangles that were written, for a partial refresh of the output window. Nothing is written if the sizes disagree.
    /// </summary>
    public static IReadOnlyList<PixelRegion> Blit(PixelBuffer canvas, CalibrationScreen screen, PixelBuffer video, IReadOnlyList<CalibrationScreen> drawn)
    {
        if (video.Width != screen.Width || video.Height != screen.Height) return Array.Empty<PixelRegion>();

        var own = new PixelRegion(screen.X, screen.Y, screen.Width, screen.Height);
        var index = IndexOf(drawn, screen.Id);
        var covers = drawn.Skip(index < 0 ? drawn.Count : index + 1).Select(s => new PixelRegion(s.X, s.Y, s.Width, s.Height));
        var written = new List<PixelRegion>();
        var canvasRect = new PixelRegion(0, 0, canvas.Width, canvas.Height);

        foreach (var part in RegionMath.VisibleParts(own, covers))
        {
            if (RegionMath.Intersect(part, canvasRect) is not { } p) continue;       // never outside the canvas, whatever the screen says
            for (var row = 0; row < p.Height; row++)
            {
                var src = ((p.Y - screen.Y + row) * video.Width + (p.X - screen.X)) * 4;
                var dst = ((p.Y + row) * canvas.Width + p.X) * 4;
                Buffer.BlockCopy(video.Data, src, canvas.Data, dst, p.Width * 4);
            }
            written.Add(p);
        }
        return written;
    }

    private static int IndexOf(IReadOnlyList<CalibrationScreen> list, Guid id)
    {
        for (var i = 0; i < list.Count; i++) if (list[i].Id == id) return i;
        return -1;
    }
}
