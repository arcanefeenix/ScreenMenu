using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LedMenu.App.ViewModels;
using LedMenu.Core.Screens;

namespace LedMenu.App.Controls;

/// <summary>
/// Draws the whole output canvas to scale with each screen as a rectangle, and lets the operator move
/// or resize a screen with the mouse. Every drag result is rounded to whole pixels; the numeric fields
/// remain the authority. Dragging is clamped to the canvas but never rewrites a screen that is left alone.
/// </summary>
public sealed class ScreenCanvasControl : FrameworkElement
{
    private const double CanvasMargin = 14;
    private const double EdgeGrab = 7;
    private const int SnapPixels = 6;

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x10, 0xB9, 0x81), Color.FromRgb(0xA8, 0x55, 0xF7),
        Color.FromRgb(0xF4, 0x72, 0xB6), Color.FromRgb(0x06, 0xB6, 0xD4), Color.FromRgb(0x84, 0xCC, 0x16),
    };

    [Flags]
    private enum Grab { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8, Move = 16 }

    private ScreensViewModel? _vm;
    private ScreenItemViewModel? _dragItem;
    private Grab _dragMode;
    private Point _dragStart;
    private (int X, int Y, int W, int H) _dragOrigin;
    private string? _readout;

    public ScreenCanvasControl()
    {
        Focusable = false;
        ClipToBounds = true;
        DataContextChanged += (_, e) =>
        {
            if (_vm != null) { _vm.Changed -= OnVmChanged; }
            _vm = e.NewValue as ScreensViewModel;
            if (_vm != null) { _vm.Changed += OnVmChanged; }
            InvalidateVisual();
        };
    }

    private void OnVmChanged() => InvalidateVisual();

    // ---- geometry helpers ------------------------------------------------------------------

    private (double Scale, double Ox, double Oy, int Cw, int Ch, bool Known) Layout()
    {
        var known = _vm?.CanvasSizeOrNull is not null;
        int cw = 1920, ch = 1080;
        if (_vm?.CanvasSizeOrNull is { } c) { cw = c.Width; ch = c.Height; }
        else if (_vm != null)
            foreach (var i in _vm.Items)
            {
                if (i.Width > 0 && i.X >= 0) cw = Math.Max(cw, (int)Math.Min(int.MaxValue / 2, (long)i.X + i.Width));
                if (i.Height > 0 && i.Y >= 0) ch = Math.Max(ch, (int)Math.Min(int.MaxValue / 2, (long)i.Y + i.Height));
            }
        var scale = Math.Min((ActualWidth - 2 * CanvasMargin) / cw, (ActualHeight - 2 * CanvasMargin) / ch);
        if (scale <= 0 || double.IsNaN(scale)) scale = 0.01;
        var ox = (ActualWidth - cw * scale) / 2;
        var oy = (ActualHeight - ch * scale) / 2;
        return (scale, ox, oy, cw, ch, known);
    }

    private static Rect ViewRect(ScreenItemViewModel i, double scale, double ox, double oy)
    {
        var w = Math.Max(i.Width, 0) * scale;
        var h = Math.Max(i.Height, 0) * scale;
        if (w < 10) w = 10;     // invalid/tiny screens stay visible and grabbable
        if (h < 10) h = 10;
        return new Rect(ox + i.X * scale, oy + i.Y * scale, w, h);
    }

    // ---- drawing --------------------------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_vm == null) return;

        var (scale, ox, oy, cw, ch, known) = Layout();
        var canvasRect = new Rect(ox, oy, cw * scale, ch * scale);
        var edge = new Pen(new SolidColorBrush(known ? Color.FromRgb(0x88, 0x88, 0x88) : Color.FromRgb(0x66, 0x66, 0x66)), 1);
        if (!known) edge.DashStyle = DashStyles.Dash;
        dc.DrawRectangle(Brushes.Black, edge, canvasRect);

        Text(dc, $"{cw}×{ch}" + (known ? "" : " (stand-in)"), canvasRect.Left + 4, canvasRect.Bottom - 20, 11,
            Color.FromRgb(0x77, 0x77, 0x77), pixelsPerDip);

        foreach (var item in _vm.Items.Where(i => !i.IsSelected).Concat(_vm.Items.Where(i => i.IsSelected)))
            DrawScreen(dc, item, scale, ox, oy, pixelsPerDip);

        if (_readout != null)
            Text(dc, _readout, 8, 6, 13, Colors.White, pixelsPerDip, new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)));
    }

    private void DrawScreen(DrawingContext dc, ScreenItemViewModel item, double scale, double ox, double oy, double ppd)
    {
        var r = ViewRect(item, scale, ox, oy);
        var baseColor = Palette[(item.Number - 1 + Palette.Length * 8) % Palette.Length];
        var disabled = !item.Enabled;

        var fill = new SolidColorBrush(Color.FromArgb(disabled ? (byte)0x30 : (byte)0xB0, baseColor.R, baseColor.G, baseColor.B));
        var outlineColor = item.Severity switch
        {
            "Error" => Color.FromRgb(0xFF, 0x3B, 0x3B),
            "Warning" => Color.FromRgb(0xFF, 0xB0, 0x20),
            _ => disabled ? Color.FromRgb(0x77, 0x77, 0x77) : Color.FromArgb(0xFF, baseColor.R, baseColor.G, baseColor.B),
        };
        var pen = new Pen(new SolidColorBrush(outlineColor), item.Severity == "None" ? 1.5 : 2.5);
        if (disabled || item.Severity == "Error") pen.DashStyle = DashStyles.Dash;
        dc.DrawRectangle(fill, pen, r);

        if (item.IsSelected)
        {
            dc.DrawRectangle(null, new Pen(Brushes.White, 1.5), Rect.Inflate(r, 2, 2));
            foreach (var h in Handles(r))
                dc.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 1), h);
        }

        if (r.Width > 36 && r.Height > 20)
        {
            dc.PushClip(new RectangleGeometry(Rect.Inflate(r, -2, -2)));
            var name = string.IsNullOrWhiteSpace(item.Name) ? "(unnamed)" : item.Name;
            Text(dc, $"{item.Number}  {name}", r.Left + 6, r.Top + 4, Math.Clamp(r.Width / 9, 11, 18), Colors.White, ppd, bold: true);
            if (r.Height > 44)
            {
                var sub = $"{item.Width}×{item.Height} at {item.X}, {item.Y}" + (disabled ? "  (disabled)" : "");
                if (item.Severity == "Error") sub = "INVALID  " + sub;
                Text(dc, sub, r.Left + 6, r.Top + 4 + Math.Clamp(r.Width / 9, 11, 18) + 4, 11, Color.FromRgb(0xEE, 0xEE, 0xEE), ppd);
            }
            dc.Pop();
        }
    }

    private static IEnumerable<Rect> Handles(Rect r)
    {
        const double s = 7;
        var xs = new[] { r.Left, r.Left + r.Width / 2, r.Right };
        var ys = new[] { r.Top, r.Top + r.Height / 2, r.Bottom };
        for (var xi = 0; xi < 3; xi++)
            for (var yi = 0; yi < 3; yi++)
                if (!(xi == 1 && yi == 1))
                    yield return new Rect(xs[xi] - s / 2, ys[yi] - s / 2, s, s);
    }

    private static void Text(DrawingContext dc, string text, double x, double y, double size, Color color,
        double ppd, Brush? background = null, bool bold = false)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            size, new SolidColorBrush(color), ppd);
        if (background != null) dc.DrawRectangle(background, null, new Rect(x - 4, y - 2, ft.Width + 8, ft.Height + 4));
        dc.DrawText(ft, new Point(x, y));
    }

    // ---- mouse ----------------------------------------------------------------------------

    private (ScreenItemViewModel? Item, Grab Mode) HitTest(Point p)
    {
        if (_vm == null) return (null, Grab.None);
        var (scale, ox, oy, _, _, _) = Layout();
        foreach (var item in _vm.Items.Where(i => i.IsSelected).Concat(_vm.Items.Reverse().Where(i => !i.IsSelected)))
        {
            var r = ViewRect(item, scale, ox, oy);
            var grabArea = item.IsSelected ? Rect.Inflate(r, EdgeGrab / 2 + 2, EdgeGrab / 2 + 2) : r;
            if (!grabArea.Contains(p)) continue;

            var g = Grab.None;
            var gx = Math.Min(EdgeGrab, r.Width / 3);
            var gy = Math.Min(EdgeGrab, r.Height / 3);
            if (p.X <= r.Left + gx) g |= Grab.Left; else if (p.X >= r.Right - gx) g |= Grab.Right;
            if (p.Y <= r.Top + gy) g |= Grab.Top; else if (p.Y >= r.Bottom - gy) g |= Grab.Bottom;
            return (item, g == Grab.None ? Grab.Move : g);
        }
        return (null, Grab.None);
    }

    private static Cursor CursorFor(Grab g) => g switch
    {
        Grab.Move => Cursors.SizeAll,
        Grab.Left or Grab.Right => Cursors.SizeWE,
        Grab.Top or Grab.Bottom => Cursors.SizeNS,
        Grab.Left | Grab.Top or Grab.Right | Grab.Bottom => Cursors.SizeNWSE,
        Grab.Right | Grab.Top or Grab.Left | Grab.Bottom => Cursors.SizeNESW,
        _ => Cursors.Arrow,
    };

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        var (item, mode) = HitTest(p);
        if (item == null) { if (_vm != null) _vm.Selected = null; return; }

        _vm!.Selected = item;
        _dragItem = item;
        _dragMode = mode;
        _dragStart = p;
        _dragOrigin = (item.X, item.Y, item.Width, item.Height);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (_dragItem == null || _vm == null)
        {
            Cursor = CursorFor(HitTest(p).Mode);
            return;
        }

        var (scale, _, _, cw, ch, known) = Layout();
        var dx = (int)Math.Round((p.X - _dragStart.X) / scale);
        var dy = (int)Math.Round((p.Y - _dragStart.Y) / scale);
        var snap = !Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt);
        var (x0, y0, w0, h0) = _dragOrigin;
        int x = x0, y = y0, w = w0, h = h0;

        var xt = new List<int> { 0 };
        var yt = new List<int> { 0 };
        if (known) { xt.Add(cw); yt.Add(ch); }
        foreach (var o in _vm.Items.Where(o => !ReferenceEquals(o, _dragItem) && o.Enabled && o.Width > 0 && o.Height > 0))
        {
            xt.Add(o.X); xt.Add(o.X + o.Width);
            yt.Add(o.Y); yt.Add(o.Y + o.Height);
        }
        var thr = Math.Max(1, (int)Math.Round(SnapPixels / scale));
        int SnapX(int v) => snap ? SnapHelper.Snap(v, xt, thr) : v;
        int SnapY(int v) => snap ? SnapHelper.Snap(v, yt, thr) : v;

        if (_dragMode == Grab.Move)
        {
            x = x0 + dx; y = y0 + dy;
            if (snap) { x = SnapHelper.SnapSpanStart(x, w0, xt, thr); y = SnapHelper.SnapSpanStart(y, h0, yt, thr); }
            if (known) { x = Math.Min(x, cw - w0); y = Math.Min(y, ch - h0); }
            x = Math.Max(0, x); y = Math.Max(0, y);
        }
        else
        {
            if (_dragMode.HasFlag(Grab.Left))
            {
                var right = x0 + w0;
                x = Math.Clamp(SnapX(x0 + dx), 0, right - 1);
                w = right - x;
            }
            if (_dragMode.HasFlag(Grab.Right))
            {
                var right = SnapX(x0 + w0 + dx);
                if (known) right = Math.Min(right, cw);
                w = Math.Max(1, right - x);
            }
            if (_dragMode.HasFlag(Grab.Top))
            {
                var bottom = y0 + h0;
                y = Math.Clamp(SnapY(y0 + dy), 0, bottom - 1);
                h = bottom - y;
            }
            if (_dragMode.HasFlag(Grab.Bottom))
            {
                var bottom = SnapY(y0 + h0 + dy);
                if (known) bottom = Math.Min(bottom, ch);
                h = Math.Max(1, bottom - y);
            }
        }

        _readout = $"X {x}   Y {y}   W {w}   H {h}";
        _dragItem.SetGeometryPreview(x, y, w, h);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragItem == null) return;
        var item = _dragItem;
        _dragItem = null;
        _readout = null;
        ReleaseMouseCapture();
        var changed = (item.X, item.Y, item.Width, item.Height) != _dragOrigin;
        if (changed) _vm?.CommitGeometry(item);
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragItem != null)
        {
            var item = _dragItem;
            _dragItem = null;
            _readout = null;
            _vm?.CommitGeometry(item);
            InvalidateVisual();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        InvalidateVisual();
    }
}
