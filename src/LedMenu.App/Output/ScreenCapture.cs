using System.Runtime.InteropServices;

namespace LedMenu.App.Output;

/// <summary>Captures a rectangle of the real desktop in physical pixels using plain GDI (no extra packages).</summary>
internal static class ScreenCapture
{
    private const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits,
        ref BITMAPINFOHEADER info, uint usage);

    /// <summary>Returns BGRA bytes (top-down, 4 bytes per pixel) for the given desktop rectangle.</summary>
    public static byte[] Capture(int x, int y, int width, int height)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, width, height, screen, x, y, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("BitBlt from the screen failed.");

            var info = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,   // negative = top-down rows
                biPlanes = 1,
                biBitCount = 32,
            };
            var bits = new byte[width * height * 4];
            if (GetDIBits(mem, bmp, 0, (uint)height, bits, ref info, 0) == 0)
                throw new InvalidOperationException("GetDIBits failed.");
            return bits;
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
