namespace LedMenu.Core.Calibration;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Red = new(255, 0, 0);
    public static readonly Rgb Green = new(0, 255, 0);
    public static readonly Rgb Blue = new(0, 0, 255);
    public static readonly Rgb Yellow = new(255, 255, 0);
    public static readonly Rgb Magenta = new(255, 0, 255);
    public static readonly Rgb Cyan = new(0, 255, 255);
    public static readonly Rgb Orange = new(255, 140, 0);
    public static readonly Rgb Gray = new(128, 128, 128);

    public override string ToString() => $"RGB({R},{G},{B})";
}

/// <summary>
/// A plain array of pixels (BGRA, opaque) with integer-only drawing. Nothing here uses WPF, DPI, or
/// anti-aliasing: every operation writes whole pixels, so a 1-pixel line is exactly 1 pixel, and the
/// result can be read back and tested. All drawing is clipped to the buffer.
/// </summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Buffer size must be positive.");
        Width = width;
        Height = height;
        Data = new byte[checked(width * height * 4)];
        for (var i = 3; i < Data.Length; i += 4) Data[i] = 255;   // opaque black
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>BGRA, top-down, 4 bytes per pixel, stride = Width * 4.</summary>
    public byte[] Data { get; }

    public Rgb GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) throw new ArgumentOutOfRangeException();
        var i = (y * Width + x) * 4;
        return new Rgb(Data[i + 2], Data[i + 1], Data[i]);
    }

    public void SetPixel(int x, int y, Rgb c)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        var i = (y * Width + x) * 4;
        Data[i] = c.B; Data[i + 1] = c.G; Data[i + 2] = c.R; Data[i + 3] = 255;
    }

    public void Clear(Rgb c) => FillRect(0, 0, Width, Height, c);

    public void FillRect(int x, int y, int w, int h, Rgb c)
    {
        if (w <= 0 || h <= 0) return;
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = (int)Math.Min((long)Width, (long)x + w);
        var y1 = (int)Math.Min((long)Height, (long)y + h);
        for (var yy = y0; yy < y1; yy++)
        {
            var i = (yy * Width + x0) * 4;
            for (var xx = x0; xx < x1; xx++, i += 4)
            {
                Data[i] = c.B; Data[i + 1] = c.G; Data[i + 2] = c.R; Data[i + 3] = 255;
            }
        }
    }

    public void HLine(int x, int y, int length, Rgb c, int thickness = 1) => FillRect(x, y, length, thickness, c);
    public void VLine(int x, int y, int length, Rgb c, int thickness = 1) => FillRect(x, y, thickness, length, c);

    /// <summary>Rectangle outline drawn inward from the edge, so a (0,0,W,H) outline covers exactly the outermost pixels.</summary>
    public void Frame(int x, int y, int w, int h, Rgb c, int thickness = 1)
    {
        if (w <= 0 || h <= 0) return;
        var t = Math.Min(thickness, Math.Min((w + 1) / 2, (h + 1) / 2));
        FillRect(x, y, w, t, c);
        FillRect(x, y + h - t, w, t, c);
        FillRect(x, y, t, h, c);
        FillRect(x + w - t, y, t, h, c);
    }

    /// <summary>Bresenham line, 1 pixel wide, no anti-aliasing.</summary>
    public void Line(int x0, int y0, int x1, int y1, Rgb c)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            SetPixel(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>Midpoint circle outline, 1 pixel wide, no anti-aliasing.</summary>
    public void Circle(int cx, int cy, int radius, Rgb c)
    {
        if (radius <= 0) { SetPixel(cx, cy, c); return; }
        int x = radius, y = 0, err = 1 - radius;
        while (x >= y)
        {
            SetPixel(cx + x, cy + y, c); SetPixel(cx + y, cy + x, c);
            SetPixel(cx - y, cy + x, c); SetPixel(cx - x, cy + y, c);
            SetPixel(cx - x, cy - y, c); SetPixel(cx - y, cy - x, c);
            SetPixel(cx + y, cy - x, c); SetPixel(cx + x, cy - y, c);
            y++;
            if (err < 0) err += 2 * y + 1;
            else { x--; err += 2 * (y - x) + 1; }
        }
    }

    /// <summary>Copies another buffer into this one with its top-left at (dx, dy), clipped.</summary>
    public void Blit(PixelBuffer src, int dx, int dy)
    {
        var sx0 = Math.Max(0, -dx);
        var sy0 = Math.Max(0, -dy);
        var sx1 = (int)Math.Min(src.Width, (long)Width - dx);
        var sy1 = (int)Math.Min(src.Height, (long)Height - dy);
        if (sx1 <= sx0 || sy1 <= sy0) return;
        for (var sy = sy0; sy < sy1; sy++)
            Buffer.BlockCopy(src.Data, (sy * src.Width + sx0) * 4,
                             Data, ((sy + dy) * Width + sx0 + dx) * 4, (sx1 - sx0) * 4);
    }

    // ---- text (built-in bitmap font, whole pixels only) -------------------------------------

    /// <summary>Draws text with the top-left of the first glyph at (x, y). <paramref name="scale"/> is a whole-number pixel multiplier.</summary>
    public void DrawText(string text, int x, int y, int scale, Rgb color, Rgb? background = null, int padding = 0)
    {
        scale = Math.Max(1, scale);
        if (background is { } bg)
            FillRect(x - padding, y - padding, PixelFont.TextWidth(text, scale) + 2 * padding, PixelFont.TextHeight(scale) + 2 * padding, bg);

        var cx = x;
        foreach (var ch in text)
        {
            var glyph = PixelFont.Glyph(ch);
            for (var row = 0; row < PixelFont.GlyphHeight; row++)
                for (var col = 0; col < PixelFont.GlyphWidth; col++)
                    if (glyph[row][col] == '#')
                        FillRect(cx + col * scale, y + row * scale, scale, scale, color);
            cx += PixelFont.Advance * scale;
        }
    }

    /// <summary>Draws text horizontally centered on <paramref name="centerX"/>.</summary>
    public void DrawTextCentered(string text, int centerX, int y, int scale, Rgb color, Rgb? background = null, int padding = 0) =>
        DrawText(text, centerX - PixelFont.TextWidth(text, scale) / 2, y, scale, color, background, padding);

    /// <summary>Draws text whose right edge is at <paramref name="rightX"/> (exclusive).</summary>
    public void DrawTextRight(string text, int rightX, int y, int scale, Rgb color, Rgb? background = null, int padding = 0) =>
        DrawText(text, rightX - PixelFont.TextWidth(text, scale), y, scale, color, background, padding);
}
