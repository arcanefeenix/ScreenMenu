namespace LedMenu.Core.Calibration;

public enum OutputMode
{
    /// <summary>What the audience sees. Until menus exist this is plain black.</summary>
    Normal,
    /// <summary>Large number, name, size and position on every drawable screen.</summary>
    IdentifyScreens,
    /// <summary>Full per-screen calibration pattern at each screen's own size.</summary>
    ScreenCalibration,
    /// <summary>One pattern covering the whole Windows output canvas, independent of screens.</summary>
    OutputCanvasCalibration,
}

public enum TestBlockKind { VerticalStripes, HorizontalStripes, Checker }

public sealed record TestBlock(TestBlockKind Kind, int X, int Y, int Width, int Height);

public sealed record CanvasAnchor(string Name, int X, int Y, Rgb Color);

/// <summary>
/// The whole-canvas pattern used to learn how the LED controller maps its HDMI input.
/// It draws source-pixel coordinates everywhere (grid labels every 100 px, rulers on all four edges,
/// nine named anchors) so a photo of the wall shows exactly which source rectangle is visible,
/// and it contains 1-pixel detail so scaling or dropped rows/columns shows up as blur or irregularity.
/// Coordinates are pixels from the top-left of the output canvas: (0,0) is the top-left pixel.
/// </summary>
public static class CanvasPattern
{
    public const int GridStep = 100;
    public const int MarkerSize = 16;
    public const int MarkerInset = 6;

    public static readonly Rgb TopBand = Rgb.Red;
    public static readonly Rgb BottomBand = Rgb.Blue;
    public static readonly Rgb LeftBand = Rgb.Green;
    public static readonly Rgb RightBand = Rgb.Yellow;

    /// <summary>The nine anchor points. X/Y are the source pixel each anchor refers to.</summary>
    public static IReadOnlyList<CanvasAnchor> Anchors(int w, int h) => new[]
    {
        new CanvasAnchor("TOP LEFT", 0, 0, Rgb.Red),
        new CanvasAnchor("TOP CENTER", w / 2, 0, Rgb.Orange),
        new CanvasAnchor("TOP RIGHT", w - 1, 0, Rgb.Yellow),
        new CanvasAnchor("CENTER LEFT", 0, h / 2, Rgb.Green),
        new CanvasAnchor("CENTER", w / 2, h / 2, Rgb.White),
        new CanvasAnchor("CENTER RIGHT", w - 1, h / 2, Rgb.Cyan),
        new CanvasAnchor("BOTTOM LEFT", 0, h - 1, Rgb.Blue),
        new CanvasAnchor("BOTTOM CENTER", w / 2, h - 1, new Rgb(160, 0, 255)),
        new CanvasAnchor("BOTTOM RIGHT", w - 1, h - 1, new Rgb(255, 105, 180)),
    };

    /// <summary>Top-left of each 16x16 anchor square.</summary>
    public static (int X, int Y) MarkerOrigin(int index, int w, int h)
    {
        var col = index % 3;
        var row = index / 3;
        var x = col == 0 ? MarkerInset : col == 1 ? w / 2 - MarkerSize / 2 : w - MarkerInset - MarkerSize;
        var y = row == 0 ? MarkerInset : row == 1 ? h / 2 - MarkerSize / 2 : h - MarkerInset - MarkerSize;
        return (x, y);
    }

    /// <summary>1-pixel detail blocks just below the centre. Under true 1:1 mapping they stay crisp; scaling blurs them.</summary>
    public static IReadOnlyList<TestBlock> TestBlocks(int w, int h)
    {
        const int bw = 64, bh = 48, gap = 20;
        var total = bw * 3 + gap * 2;
        var x0 = w / 2 - total / 2;
        var y0 = h / 2 + 40;
        return new[]
        {
            new TestBlock(TestBlockKind.VerticalStripes, x0, y0, bw, bh),
            new TestBlock(TestBlockKind.HorizontalStripes, x0 + bw + gap, y0, bw, bh),
            new TestBlock(TestBlockKind.Checker, x0 + 2 * (bw + gap), y0, bw, bh),
        };
    }

    public static PixelBuffer Render(int w, int h)
    {
        var buf = new PixelBuffer(w, h);
        var (vx, vw) = ScreenPatterns.CenterSpan(w);
        var (hy, hh) = ScreenPatterns.CenterSpan(h);

        // coordinate grid: a line every 100 source pixels, brighter every 500
        var minor = new Rgb(48, 48, 48);
        var major = new Rgb(115, 115, 115);
        for (var x = 0; x < w; x += GridStep) buf.VLine(x, 0, h, x % 500 == 0 ? major : minor);
        for (var y = 0; y < h; y += GridStep) buf.HLine(0, y, w, y % 500 == 0 ? major : minor);

        // shape checks: circles turn into ellipses if the aspect ratio is changed
        var circle = new Rgb(0, 140, 220);
        buf.Circle(w / 2, h / 2, Math.Min(w, h) * 45 / 100, circle);
        buf.Circle(w / 2, h / 2, Math.Min(w, h) * 15 / 100, circle);
        var diag = new Rgb(80, 80, 80);
        buf.Line(0, 0, w - 1, h - 1, diag);
        buf.Line(w - 1, 0, 0, h - 1, diag);

        // centre lines, exactly on the middle pixels
        buf.VLine(vx, 0, h, Rgb.Magenta, vw);
        buf.HLine(0, hy, w, Rgb.Magenta, hh);

        // coloured edge bands: if the controller overscans or crops, the missing colour tells you which edge
        buf.FillRect(1, 1, w - 2, 4, TopBand);
        buf.FillRect(1, h - 5, w - 2, 4, BottomBand);
        buf.FillRect(1, 5, 4, h - 10, LeftBand);
        buf.FillRect(w - 5, 5, 4, h - 10, RightBand);

        DrawRulers(buf, w, h);
        DrawGridLabels(buf, w, h);
        DrawTestBlocks(buf, w, h);
        DrawCentreText(buf, w, h, vx, vw, hy, hh);
        DrawAnchors(buf, w, h);

        buf.Frame(0, 0, w, h, Rgb.White, 1);   // exact one-pixel outer boundary, drawn last
        return buf;
    }

    // ---- parts -------------------------------------------------------------------------------

    private static void DrawRulers(PixelBuffer buf, int w, int h)
    {
        var tick = new Rgb(210, 210, 210);
        static int Len(int v) => v % 100 == 0 ? 20 : v % 50 == 0 ? 12 : 6;
        for (var x = 0; x < w; x += 10)
        {
            buf.VLine(x, 5, Len(x), tick);                      // top, pointing in
            buf.VLine(x, h - 5 - Len(x), Len(x), tick);         // bottom, pointing in
        }
        for (var y = 0; y < h; y += 10)
        {
            buf.HLine(5, y, Len(y), tick);                      // left
            buf.HLine(w - 5 - Len(y), y, Len(y), tick);         // right
        }
        // numbers every 100 along each edge (source coordinates)
        for (var x = GridStep; x < w - 40; x += GridStep)
        {
            var t = x.ToString();
            buf.DrawTextCentered(t, x, 28, 1, Rgb.White, Rgb.Black, 1);
            buf.DrawTextCentered(t, x, h - 28 - PixelFont.TextHeight(1), 1, Rgb.White, Rgb.Black, 1);
        }
        for (var y = GridStep; y < h - 40; y += GridStep)
        {
            var t = y.ToString();
            buf.DrawText(t, 28, y - 3, 1, Rgb.White, Rgb.Black, 1);
            buf.DrawTextRight(t, w - 28, y - 3, 1, Rgb.White, Rgb.Black, 1);
        }
    }

    private static void DrawGridLabels(PixelBuffer buf, int w, int h)
    {
        for (var gx = GridStep; gx <= w - GridStep; gx += GridStep)
            for (var gy = GridStep; gy <= h - GridStep; gy += GridStep)
            {
                // every 200 px intersection gets a double-size label that is easier to read in a photo of the wall
                var big = gx % 200 == 0 && gy % 200 == 0;
                buf.DrawText($"{gx},{gy}", gx + 3, gy + 3, big ? 2 : 1, new Rgb(255, 255, 0), Rgb.Black, 1);
            }
    }

    private static void DrawTestBlocks(PixelBuffer buf, int w, int h)
    {
        foreach (var b in TestBlocks(w, h))
        {
            buf.FillRect(b.X - 2, b.Y - 2, b.Width + 4, b.Height + 4, Rgb.Black);
            for (var y = 0; y < b.Height; y++)
                for (var x = 0; x < b.Width; x++)
                {
                    var on = b.Kind switch
                    {
                        TestBlockKind.VerticalStripes => x % 2 == 0,
                        TestBlockKind.HorizontalStripes => y % 2 == 0,
                        _ => (x + y) % 2 == 0,
                    };
                    if (on) buf.SetPixel(b.X + x, b.Y + y, Rgb.White);
                }
            var caption = b.Kind switch
            {
                TestBlockKind.VerticalStripes => "1PX COLUMNS",
                TestBlockKind.HorizontalStripes => "1PX ROWS",
                _ => "1PX CHECKER",
            };
            buf.DrawTextCentered(caption, b.X + b.Width / 2, b.Y + b.Height + 5, 1, Rgb.White, Rgb.Black, 1);
        }
    }

    private static void DrawCentreText(PixelBuffer buf, int w, int h, int vx, int vw, int hy, int hh)
    {
        var cx = w / 2;
        var cy = h / 2;
        var title = $"OUTPUT CANVAS {w} × {h}";
        var tScale = PixelFont.FitScale(title, w - 120, 4);

        if (h >= 700)
        {
            buf.DrawTextCentered(title, cx, cy - 250, tScale, Rgb.White, Rgb.Black, 3);
            buf.DrawTextCentered("TOP LEFT SOURCE PIXEL IS 0,0", cx, cy - 250 + PixelFont.TextHeight(tScale) + 12, 2, Rgb.Cyan, Rgb.Black, 2);
            buf.DrawTextCentered($"CENTER LINES = PIXELS {vx}{(vw > 1 ? "-" + (vx + vw - 1) : "")} ACROSS, {hy}{(hh > 1 ? "-" + (hy + hh - 1) : "")} DOWN",
                cx, cy - 250 + PixelFont.TextHeight(tScale) + 12 + 22, 2, Rgb.Cyan, Rgb.Black, 2);
        }
        else
        {
            buf.DrawTextCentered(title, cx, Math.Max(40, cy - 90), Math.Min(tScale, 2), Rgb.White, Rgb.Black, 2);
        }

        // centre marker
        buf.FillRect(vx - 5, hy - 5, vw + 10, hh + 10, Rgb.White);
        buf.FillRect(vx - 3, hy - 3, vw + 6, hh + 6, Rgb.Magenta);
        buf.DrawTextCentered("CENTER", cx, cy - 56, 3, Rgb.White, Rgb.Black, 3);
        buf.DrawTextCentered($"{cx},{cy}", cx, cy - 30, 2, Rgb.Yellow, Rgb.Black, 2);
    }

    private static void DrawAnchors(PixelBuffer buf, int w, int h)
    {
        var anchors = Anchors(w, h);
        for (var i = 0; i < anchors.Count; i++)
        {
            if (i == 4) continue;   // CENTER is drawn with the centre text
            var a = anchors[i];
            var (mx, my) = MarkerOrigin(i, w, h);
            buf.FillRect(mx - 1, my - 1, MarkerSize + 2, MarkerSize + 2, Rgb.Black);
            buf.FillRect(mx, my, MarkerSize, MarkerSize, a.Color);

            var col = i % 3;
            var row = i / 3;
            var coords = $"{a.X},{a.Y}";
            const int nameScale = 3, coordScale = 2;
            var nameH = PixelFont.TextHeight(nameScale);
            var coordH = PixelFont.TextHeight(coordScale);

            int nameY, coordY;
            if (row == 0) { nameY = MarkerInset + 1; coordY = nameY + nameH + 4; }
            else if (row == 1) { nameY = h / 2 - nameH / 2 - 8; coordY = nameY + nameH + 4; }
            else { coordY = h - MarkerInset - coordH - 1; nameY = coordY - nameH - 4; }

            // beside the square for left/right columns, below/above it for the middle column
            if (col == 0)
            {
                var x = MarkerInset + MarkerSize + 8;
                buf.DrawText(a.Name, x, nameY, nameScale, a.Color, Rgb.Black, 2);
                buf.DrawText(coords, x, coordY, coordScale, Rgb.White, Rgb.Black, 2);
            }
            else if (col == 2)
            {
                var x = w - MarkerInset - MarkerSize - 8;
                buf.DrawTextRight(a.Name, x, nameY, nameScale, a.Color, Rgb.Black, 2);
                buf.DrawTextRight(coords, x, coordY, coordScale, Rgb.White, Rgb.Black, 2);
            }
            else
            {
                var ny = row == 0 ? MarkerInset + MarkerSize + 6 : nameY;
                var cy2 = row == 0 ? ny + nameH + 4 : coordY;
                if (row == 2) { ny = nameY - MarkerSize - 6; cy2 = coordY - MarkerSize - 6; }
                buf.DrawTextCentered(a.Name, w / 2, ny, nameScale, a.Color, Rgb.Black, 2);
                buf.DrawTextCentered(coords, w / 2, cy2, coordScale, Rgb.White, Rgb.Black, 2);
            }
        }
    }
}

/// <summary>Builds the complete picture for the output canvas for a given mode.</summary>
public static class FrameComposer
{
    public static PixelBuffer Compose(OutputMode mode, int canvasWidth, int canvasHeight, IReadOnlyList<CalibrationScreen> screens)
    {
        if (mode == OutputMode.OutputCanvasCalibration)
            return CanvasPattern.Render(canvasWidth, canvasHeight);

        var frame = new PixelBuffer(canvasWidth, canvasHeight);   // pure black base
        if (mode == OutputMode.Normal) return frame;

        foreach (var s in screens)
        {
            if (s.Width <= 0 || s.Height <= 0) continue;
            var pattern = mode == OutputMode.IdentifyScreens ? ScreenPatterns.Identify(s) : ScreenPatterns.Calibration(s);
            frame.Blit(pattern, s.X, s.Y);
        }
        return frame;
    }
}
