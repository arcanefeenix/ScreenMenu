using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LedMenu.App.Display;
using LedMenu.Core.Calibration;
using LedMenu.Core.Display;
using LedMenu.Core.Logging;
using static LedMenu.App.Display.NativeMethods;

namespace LedMenu.App.Output;

/// <summary>
/// The borderless LED output surface. It is positioned in physical pixels with SetWindowPos, never
/// through WPF's device-independent Left/Top/Width/Height, so the result does not depend on which DPI
/// the operator monitor uses. It is shown without activation so it does not take keyboard focus.
/// </summary>
public partial class OutputWindow : Window
{
    private readonly IAppLog _log;
    private DisplayInfo _display;
    private HwndSource? _source;
    private int _placing;

    public OutputWindow(DisplayInfo display, IAppLog log)
    {
        InitializeComponent();
        _display = display;
        _log = log;

        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Raised when the operator deliberately asks, from this window, to stop output.</summary>
    public event Action? StopRequested;

    public IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = Hwnd;
        // No rounded corners and no DWM border: the surface must be an exact rectangle of pure black.
        var doNotRound = 1;                     // DWMWCP_DONOTROUND
        DwmSetWindowAttribute(hwnd, 33, ref doNotRound, sizeof(int));      // DWMWA_WINDOW_CORNER_PREFERENCE
        var noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int));        // DWMWA_BORDER_COLOR

        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        Place();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Place();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // After WPF has processed a DPI change (which resizes the window to Windows' suggestion),
        // put the window back on the exact display bounds.
        if (msg == WM_DPICHANGED)
            Dispatcher.BeginInvoke(() => { Place(); ShowFrame(_frame); }, DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    /// <summary>Moves the window to the display's exact physical bounds (re-applies a few times if Windows adjusts it).</summary>
    public void Place(DisplayInfo? newDisplay = null)
    {
        if (newDisplay != null) _display = newDisplay;
        var hwnd = Hwnd;
        if (hwnd == IntPtr.Zero || _placing > 0) return;
        _placing++;
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, _display.X, _display.Y, _display.Width, _display.Height, SWP_NOACTIVATE);
                GetWindowRect(hwnd, out var r);
                if (r.Left == _display.X && r.Top == _display.Y &&
                    r.Right - r.Left == _display.Width && r.Bottom - r.Top == _display.Height)
                    break;
            }
        }
        finally { _placing--; }
    }

    public OutputGeometryReport BuildReport()
    {
        var hwnd = Hwnd;
        GetWindowRect(hwnd, out var w);
        GetClientRect(hwnd, out var c);
        var dpi = VisualTreeHelper.GetDpi(this);
        uint mx = 0, my = 0;
        GetDpiForMonitor(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), MDT_EFFECTIVE_DPI, out mx, out my);

        return OutputGeometry.Evaluate(
            new PixelRect(_display.X, _display.Y, _display.Width, _display.Height),
            new PixelRect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top),
            c.Right - c.Left, c.Bottom - c.Top,
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, (int)mx, (int)my);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Only reachable if the operator deliberately clicked this window (it is never activated on its own).
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            StopRequested?.Invoke();
        }
    }

    // ---- frames (calibration patterns now, menus later) --------------------------------------

    private PixelBuffer? _frame;

    /// <summary>
    /// Shows a frame at exact 1:1 physical pixels, or pure black when null. The bitmap carries the window's
    /// own DPI, so its natural size in WPF units is exactly its pixel size on this monitor.
    /// </summary>
    public void ShowFrame(PixelBuffer? frame)
    {
        _frame = frame;
        Surface.Children.Clear();
        if (frame == null) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var bmp = BitmapSource.Create(frame.Width, frame.Height, dpi.PixelsPerInchX, dpi.PixelsPerInchY,
            PixelFormats.Bgra32, null, frame.Data, frame.Width * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Surface.Children.Add(image);
    }

    /// <summary>Self-test pattern covering the whole client area.</summary>
    public void ShowProbe()
    {
        GetClientRect(Hwnd, out var c);
        var w = c.Right - c.Left;
        var h = c.Bottom - c.Top;
        var probe = new PixelBuffer(w, h);
        Buffer.BlockCopy(ProbePattern.Render(w, h), 0, probe.Data, 0, probe.Data.Length);
        ShowFrame(probe);
    }

    public void ClearProbe() => ShowFrame(null);

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(WndProc);
        base.OnClosed(e);
    }
}

/// <summary>Known pixel pattern: 1-px red border, distinct corner pixels, everything else pure black.</summary>
public static class ProbePattern
{
    public static (byte B, byte G, byte R) ExpectedAt(int x, int y, int w, int h)
    {
        bool left = x == 0, right = x == w - 1, top = y == 0, bottom = y == h - 1;
        if (left && top) return (255, 255, 255);        // white
        if (right && top) return (0, 255, 0);           // green
        if (left && bottom) return (255, 0, 0);         // blue (BGR)
        if (right && bottom) return (0, 255, 255);      // yellow
        if (left || right || top || bottom) return (0, 0, 255); // red
        return (0, 0, 0);
    }

    public static byte[] Render(int w, int h)
    {
        var buf = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (b, g, r) = ExpectedAt(x, y, w, h);
                var i = (y * w + x) * 4;
                buf[i] = b; buf[i + 1] = g; buf[i + 2] = r; buf[i + 3] = 255;
            }
        return buf;
    }
}
