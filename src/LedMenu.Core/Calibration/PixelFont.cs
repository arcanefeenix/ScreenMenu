namespace LedMenu.Core.Calibration;

/// <summary>
/// A 5x7 bitmap font. Glyphs are drawn as whole-pixel squares (never smoothed), so text on an LED wall
/// is made of exactly the LEDs the pattern intends. Lower case is shown as upper case; any character
/// without a glyph is shown as '?'.
/// </summary>
public static class PixelFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;
    /// <summary>Distance from one glyph origin to the next, in unscaled pixels (glyph plus one blank column).</summary>
    public const int Advance = 6;

    private static readonly Dictionary<char, string[]> Glyphs = Build();

    public static string[] Glyph(char c)
    {
        if (c == '×') c = 'X';
        c = char.ToUpperInvariant(c);
        return Glyphs.TryGetValue(c, out var g) ? g : Glyphs['?'];
    }

    public static bool HasGlyph(char c)
    {
        if (c == '×') return true;
        return Glyphs.ContainsKey(char.ToUpperInvariant(c));
    }

    public static int TextWidth(string text, int scale) =>
        text.Length == 0 ? 0 : (text.Length * Advance - 1) * Math.Max(1, scale);

    public static int TextHeight(int scale) => GlyphHeight * Math.Max(1, scale);

    /// <summary>Largest whole-number scale (at least 1, at most <paramref name="max"/>) at which the text fits the width.</summary>
    public static int FitScale(string text, int availableWidth, int max)
    {
        var best = 1;
        for (var s = 2; s <= max; s++)
            if (TextWidth(text, s) <= availableWidth) best = s;
        return best;
    }

    public static IEnumerable<char> SupportedCharacters => Glyphs.Keys;

    private static Dictionary<char, string[]> Build()
    {
        var d = new Dictionary<char, string[]>();
        void G(char c, string rows) => d[c] = rows.Split('/');

        G('0', ".###./#...#/#..##/#.#.#/##..#/#...#/.###.");
        G('1', "..#../.##../..#../..#../..#../..#../.###.");
        G('2', ".###./#...#/....#/...#./..#../.#.../#####");
        G('3', "#####/...#./..#../...#./....#/#...#/.###.");
        G('4', "...#./..##./.#.#./#..#./#####/...#./...#.");
        G('5', "#####/#..../####./....#/....#/#...#/.###.");
        G('6', "..##./.#.../#..../####./#...#/#...#/.###.");
        G('7', "#####/....#/...#./..#../.#.../.#.../.#...");
        G('8', ".###./#...#/#...#/.###./#...#/#...#/.###.");
        G('9', ".###./#...#/#...#/.####/....#/...#./.##..");
        G('A', ".###./#...#/#...#/#####/#...#/#...#/#...#");
        G('B', "####./#...#/#...#/####./#...#/#...#/####.");
        G('C', ".###./#...#/#..../#..../#..../#...#/.###.");
        G('D', "####./#...#/#...#/#...#/#...#/#...#/####.");
        G('E', "#####/#..../#..../####./#..../#..../#####");
        G('F', "#####/#..../#..../####./#..../#..../#....");
        G('G', ".###./#...#/#..../#.###/#...#/#...#/.####");
        G('H', "#...#/#...#/#...#/#####/#...#/#...#/#...#");
        G('I', ".###./..#../..#../..#../..#../..#../.###.");
        G('J', "..###/...#./...#./...#./...#./#..#./.##..");
        G('K', "#...#/#..#./#.#../##.../#.#../#..#./#...#");
        G('L', "#..../#..../#..../#..../#..../#..../#####");
        G('M', "#...#/##.##/#.#.#/#.#.#/#...#/#...#/#...#");
        G('N', "#...#/#...#/##..#/#.#.#/#..##/#...#/#...#");
        G('O', ".###./#...#/#...#/#...#/#...#/#...#/.###.");
        G('P', "####./#...#/#...#/####./#..../#..../#....");
        G('Q', ".###./#...#/#...#/#...#/#.#.#/#..#./.##.#");
        G('R', "####./#...#/#...#/####./#.#../#..#./#...#");
        G('S', ".####/#..../#..../.###./....#/....#/####.");
        G('T', "#####/..#../..#../..#../..#../..#../..#..");
        G('U', "#...#/#...#/#...#/#...#/#...#/#...#/.###.");
        G('V', "#...#/#...#/#...#/#...#/#...#/.#.#./..#..");
        G('W', "#...#/#...#/#...#/#.#.#/#.#.#/##.##/#...#");
        G('X', "#...#/#...#/.#.#./..#../.#.#./#...#/#...#");
        G('Y', "#...#/#...#/.#.#./..#../..#../..#../..#..");
        G('Z', "#####/....#/...#./..#../.#.../#..../#####");
        G(' ', "...../...../...../...../...../...../.....");
        G('-', "...../...../...../#####/...../...../.....");
        G('.', "...../...../...../...../...../.##../.##..");
        G(',', "...../...../...../...../.##../..#../.#...");
        G(':', "...../..#../...../...../..#../...../.....");
        G('(', "...#./..#../.#.../.#.../.#.../..#../...#.");
        G(')', ".#.../..#../...#./...#./...#./..#../.#...");
        G('/', "....#/....#/...#./..#../.#.../#..../#....");
        G('=', "...../...../#####/...../#####/...../.....");
        G('+', "...../..#../..#../#####/..#../..#../.....");
        G('#', ".#.#./.#.#./#####/.#.#./#####/.#.#./.#.#.");
        G('%', "##..#/##..#/...#./..#../.#.../#..##/#..##");
        G('_', "...../...../...../...../...../...../#####");
        G('?', ".###./#...#/....#/...#./..#../...../..#..");
        return d;
    }
}
