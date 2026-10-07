using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LedMenu.Core.Display;
using static LedMenu.App.Display.NativeMethods;

namespace LedMenu.App.Display;

/// <summary>
/// Small on-top label centered on one physical display. Not fullscreen, never takes focus, and
/// closes itself, so it cannot be mistaken for LED output or trap the operator.
/// </summary>
public partial class IdentifyWindow : Window
{
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private const int WidthPx = 640, HeightPx = 360;

    private readonly DisplayInfo _display;
    private readonly DispatcherTimer _timer;

    public IdentifyWindow(DisplayInfo display, int number, TimeSpan duration)
    {
        InitializeComponent();
        _display = display;
        NumberText.Text = number.ToString();
        NameText.Text = display.FriendlyName;
        DetailText.Text = $"{display.Resolution}  •  {display.ScalePercent}%" + (display.IsPrimary ? "  •  Windows primary" : "");

        SourceInitialized += (_, _) => Place();
        ContentRendered += (_, _) => Place();   // WPF may rescale on a DPI change; re-assert physical size
        MouseDown += (_, _) => Close();

        _timer = new DispatcherTimer { Interval = duration };
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Centers the window on its display using physical pixels, independent of any DPI scaling.</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var x = _display.X + (_display.Width - WidthPx) / 2;
        var y = _display.Y + (_display.Height - HeightPx) / 2;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, WidthPx, HeightPx, SWP_NOZORDER | SWP_NOACTIVATE);
    }
}
