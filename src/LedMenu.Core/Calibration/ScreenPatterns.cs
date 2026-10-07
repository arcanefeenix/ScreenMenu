namespace LedMenu.Core.Calibration;

/// <summary>What the calibration renderer needs to know about one screen. Numbers match the list order in the operator window.</summary>
public sealed record CalibrationScreen(Guid Id, int Number, string Name, int X, int Y, int Width, int Height, Guid? MenuId = null, bool IsVideo = false);

/// <summary>
/// Per-screen patterns. Each is generated at the screen's own configured width and height (never scaled
/// from a generic image) and then placed at the screen's X,Y in the output canvas.
/// </summary>
public static class ScreenPatterns
{
    private static readonly Rgb[] NumberColors =
    {
        new(59, 130, 246), new(16, 185, 129), new(168, 85, 247),
        new(244, 114, 182), new(6, 182, 212), new(132, 204, 22),
    };

    public static Rgb ColorFor(int number) => NumberColors[((number - 1) % NumberColors.Length + NumberColors.Length) % NumberColors.Length];

    /// <summary>The pixel span that is the centre of a dimension: one pixel if odd, the two middle pixels if even.</summary>
    public static (int Start, int Length) CenterSpan(int n) => n % 2 == 0 ? (n / 2 - 1, 2) : ((n - 1) / 2, 1);

    // ---- Identify ---------------------------------------------------------------------------

    public static PixelBuffer Identify(CalibrationScreen s)
    {
        int w = s.Width, h = s.Height;
        var buf = new PixelBuffer(w, h);
        var c = ColorFor(s.Number);
        buf.Clear(new Rgb((byte)(c.R * 40 / 100), (byte)(c.G * 40 / 100), (byte)(c.B * 40 / 100)));

        var lines = new List<(string Text, int MaxScale, Rgb Color)>
        {
            ("SCREEN", 4, Rgb.White),
            (s.Number.ToString(), 20, Rgb.White),
            (string.IsNullOrWhiteSpace(s.Name) ? "(UNNAMED)" : s.Name.Trim(), 3, Rgb.Yellow),
            ($"{w} × {h}", 3, Rgb.White),
            ($"X {s.X}  Y {s.Y}", 2, new Rgb(200, 200, 200)),
        };
        DrawStack(buf, lines, w, h, topPad: 6, heightFraction: 1.0);
        buf.Frame(0, 0, w, h, Rgb.White, 1);
        return buf;
    }

    // ---- Calibration ------------------------------------------------------------------------

    public static PixelBuffer Calibration(CalibrationScreen s)
    {
        int w = s.Width, h = s.Height;
        var buf = new PixelBuffer(w, h);

        // grid: every 16 px dim, every 64 px brighter. Lines start at 0, so grid coordinates are screen pixel coordinates.
        var minor = new Rgb(56, 56, 56);
        var major = new Rgb(120, 120, 120);
        for (var x = 0; x < w; x += 16) buf.VLine(x, 0, h, x % 64 == 0 ? major : minor);
        for (var y = 0; y < h; y += 16) buf.HLine(0, y, w, y % 64 == 0 ? major : minor);

        // diagonals and a circle: stretching or cropping becomes obvious
        var diag = new Rgb(90, 90, 90);
        buf.Line(0, 0, w - 1, h - 1, diag);
        buf.Line(w - 1, 0, 0, h - 1, diag);
        buf.Circle(w / 2, h / 2, Math.Min(w, h) * 3 / 8, new Rgb(0, 150, 255));

        // centre lines (two pixels wide when the dimension is even so they sit exactly on the middle)
        var (vx, vw) = CenterSpan(w);
        var (hy, hh) = CenterSpan(h);
        buf.VLine(vx, 0, h, Rgb.Magenta, vw);
        buf.HLine(0, hy, w, Rgb.Magenta, hh);
        buf.FillRect(vx - 2, hy - 2, vw + 4, hh + 4, Rgb.White);       // centre box
        buf.FillRect(vx - 1, hy - 1, vw + 2, hh + 2, Rgb.Magenta);

        // corner markers: filled squares, one colour per corner, so a missing row or column is visible
        var m = Math.Clamp(Math.Min(w, h) / 12, 5, 20);
        buf.FillRect(0, 0, m, m, Rgb.Red);
        buf.FillRect(w - m, 0, m, m, Rgb.Green);
        buf.FillRect(0, h - m, m, m, Rgb.Blue);
        buf.FillRect(w - m, h - m, m, m, Rgb.Yellow);

        // exact one-pixel outer boundary
        buf.Frame(0, 0, w, h, Rgb.White, 1);

        // local pixel coordinates of the four corners and centre
        var label = new Rgb(255, 255, 255);
        buf.DrawText("0,0", m + 3, 3, 1, label, Rgb.Black, 1);
        buf.DrawTextRight($"{w - 1},0", w - m - 3, 3, 1, label, Rgb.Black, 1);
        buf.DrawText($"0,{h - 1}", m + 3, h - 3 - PixelFont.TextHeight(1), 1, label, Rgb.Black, 1);
        buf.DrawTextRight($"{w - 1},{h - 1}", w - m - 3, h - 3 - PixelFont.TextHeight(1), 1, label, Rgb.Black, 1);
        buf.DrawTextCentered($"{w / 2},{h / 2}", w / 2, hy + hh + 6, 1, label, Rgb.Black, 1);

        // identity block
        var lines = new List<(string, int, Rgb)>
        {
            (s.Number.ToString(), 20, Rgb.White),
            (string.IsNullOrWhiteSpace(s.Name) ? "(UNNAMED)" : s.Name.Trim(), 3, Rgb.Yellow),
            ($"{w} × {h}", 3, Rgb.White),
            ($"OUTPUT X {s.X}  Y {s.Y}", 2, new Rgb(0, 255, 255)),
        };
        DrawStack(buf, lines, w, h, topPad: m + 16, heightFraction: 0.40);
        buf.Frame(0, 0, w, h, Rgb.White, 1);   // keep the outer ring pure even if text reached it
        return buf;
    }

    // ---- text stack -------------------------------------------------------------------------

    /// <summary>
    /// Draws centred lines, one under another. Each line gets the largest whole-number scale that fits the
    /// width (and, for the number, a share of the height); lower-priority lines are dropped if everything cannot fit.
    /// </summary>
    private static void DrawStack(PixelBuffer buf, List<(string Text, int MaxScale, Rgb Color)> lines,
        int w, int h, int topPad, double heightFraction)
    {
        var avail = w - 10;
        var budget = (int)(h * heightFraction) - topPad;
        var gap = 4;

        var items = new List<(string Text, int Scale, Rgb Color)>();
        foreach (var (text, maxScale, color) in lines)
        {
            var t = TruncateToWidth(text, avail);
            var scale = PixelFont.FitScale(t, avail, maxScale);
            // a huge digit must not eat the whole screen
            while (scale > 1 && PixelFont.TextHeight(scale) > Math.Max(budget * 45 / 100, PixelFont.GlyphHeight))
                scale--;
            items.Add((t, scale, color));
        }

        int Total(List<(string Text, int Scale, Rgb Color)> l) => l.Sum(i => PixelFont.TextHeight(i.Scale)) + gap * Math.Max(0, l.Count - 1);
        while (items.Count > 1 && Total(items) > budget) items.RemoveAt(items.Count - 1);

        var y = topPad;
        if (heightFraction >= 1.0) y = Math.Max(topPad, (h - Total(items)) / 2);   // identify: centre vertically
        foreach (var (text, scale, color) in items)
        {
            buf.DrawTextCentered(text, w / 2, y, scale, color, Rgb.Black, 2);
            y += PixelFont.TextHeight(scale) + gap;
        }
    }

    internal static string TruncateToWidth(string text, int width)
    {
        if (PixelFont.TextWidth(text, 1) <= width) return text;
        var t = text;
        while (t.Length > 1 && PixelFont.TextWidth(t + "..", 1) > width) t = t[..^1];
        return t + "..";
    }
}
