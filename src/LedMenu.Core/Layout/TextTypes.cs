using LedMenu.Core.Calibration;

namespace LedMenu.Core.Layout;

public enum FontWeightKind { Regular, Bold }

/// <summary>A font size in pixels (1 em = this many LED pixels) and weight. The family is chosen per menu.</summary>
public readonly record struct TextStyle(double Size, FontWeightKind Weight);

/// <summary>
/// Measures text for layout. The real implementation uses the same font engine as the renderer, so a page
/// break decided here is exactly where the drawn text ends. Tests use a simple fake with exact numbers.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>Height of one line of text in this style, in pixels.</summary>
    double LineHeight(TextStyle style);

    /// <summary>Width of a single line of text, in pixels.</summary>
    double Width(string text, TextStyle style);
}

/// <summary>A size in whole pixels, used for the logo's natural dimensions.</summary>
public readonly record struct LogoMetrics(int PixelWidth, int PixelHeight);

public abstract record DrawOp;

/// <summary>A solid rectangle.</summary>
public sealed record FillOp(int X, int Y, int Width, int Height, Rgb Color) : DrawOp;

/// <summary>One line of text whose top-left corner is at (X, Y).</summary>
public sealed record TextOp(string Text, int X, int Y, TextStyle Style, Rgb Color) : DrawOp;

/// <summary>The logo, to be drawn scaled (aspect preserved) into exactly this rectangle.</summary>
public sealed record LogoOp(int X, int Y, int Width, int Height) : DrawOp;

public static class TextWrapping
{
    /// <summary>
    /// Greedy word wrap. Lines break at spaces; a single word wider than <paramref name="maxWidth"/> is split by character
    /// (never cut off). Explicit line breaks in the text are honored. Always returns at least one line.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string? text, double maxWidth, Func<string, double> width)
    {
        var result = new List<string>();
        var paragraphs = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var paragraph in paragraphs)
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { result.Add(""); continue; }

            var line = "";
            foreach (var word in words)
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (width(candidate) <= maxWidth) { line = candidate; continue; }

                if (line.Length > 0) { result.Add(line); line = ""; }
                // the word alone may still be too wide: split it by character
                var rest = word;
                while (width(rest) > maxWidth && rest.Length > 1)
                {
                    var take = rest.Length - 1;
                    while (take > 1 && width(rest[..take]) > maxWidth) take--;
                    result.Add(rest[..take]);
                    rest = rest[take..];
                }
                line = rest;
            }
            if (line.Length > 0) result.Add(line);
        }
        return result;
    }
}
