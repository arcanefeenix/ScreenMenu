using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LedMenu.Core.Layout;

namespace LedMenu.Rendering;

/// <summary>
/// Text measurement and formatting with WPF's own text engine, in GDI-compatible "display" mode at 1 device pixel
/// per unit. The renderer draws with exactly the same formatting, so measured widths and heights are what is drawn.
/// </summary>
public sealed class WpfTextMeasurer : ITextMeasurer
{
    private readonly FontFamily _family;
    private readonly Dictionary<(string, double, FontWeightKind), double> _widths = new();
    private readonly Dictionary<(double, FontWeightKind), double> _heights = new();

    public WpfTextMeasurer(FontFamily family) => _family = family;

    public double LineHeight(TextStyle style)
    {
        var key = (style.Size, style.Weight);
        if (!_heights.TryGetValue(key, out var h))
            _heights[key] = h = Format("Xg", style, Brushes.White).Height;
        return h;
    }

    public double Width(string text, TextStyle style)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var key = (text, style.Size, style.Weight);
        if (!_widths.TryGetValue(key, out var w))
            _widths[key] = w = Format(text, style, Brushes.White).WidthIncludingTrailingWhitespace;
        return w;
    }

    /// <summary>The same formatted text used for measuring and for drawing.</summary>
    public FormattedText Format(string text, TextStyle style, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(_family, FontStyles.Normal,
                style.Weight == FontWeightKind.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            style.Size, brush, new NumberSubstitution(), TextFormattingMode.Display, 1.0);
}
