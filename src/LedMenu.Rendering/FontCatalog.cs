using System.IO;
using System.Windows.Media;

namespace LedMenu.Rendering;

public sealed record FontChoice(FontFamily Family, string Name, string? Warning);

/// <summary>
/// The fonts the renderer may use: every font file in the Fonts folder next to the program, loaded by file so
/// the layout is the same on every PC. A menu asks for a family by name; an unknown or missing family falls back to
/// the default (Lato) with a warning, and if even the bundled fonts are missing, to a system font with a warning.
/// </summary>
public sealed class FontCatalog
{
    public const string DefaultFamily = "Lato";
    private const string LastResort = "Segoe UI";

    private readonly Dictionary<string, FontFamily> _families = new(StringComparer.OrdinalIgnoreCase);

    public FontCatalog(string fontsDirectory)
    {
        Directory = fontsDirectory;
        try
        {
            if (System.IO.Directory.Exists(fontsDirectory))
            {
                var uri = new Uri(Path.GetFullPath(fontsDirectory).TrimEnd('\\', '/') + "\\");
                foreach (var family in Fonts.GetFontFamilies(uri))
                    foreach (var name in family.FamilyNames.Values)
                        _families.TryAdd(name, family);
            }
        }
        catch (Exception)
        {
            // an unreadable fonts folder behaves like an empty one; Resolve reports it
            _families.Clear();
        }
    }

    public string Directory { get; }

    public IReadOnlyList<string> Families => _families.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    public FontChoice Resolve(string? requested)
    {
        var wanted = string.IsNullOrWhiteSpace(requested) ? DefaultFamily : requested.Trim();

        if (_families.TryGetValue(wanted, out var family))
            return new FontChoice(family, wanted, null);

        if (_families.TryGetValue(DefaultFamily, out var lato))
        {
            var warning = string.Equals(wanted, DefaultFamily, StringComparison.OrdinalIgnoreCase)
                ? null
                : $"The font \"{wanted}\" was not found in the Fonts folder, so {DefaultFamily} is being used instead.";
            return new FontChoice(lato, DefaultFamily, warning);
        }

        return new FontChoice(new FontFamily(LastResort), LastResort,
            $"The bundled font {DefaultFamily} could not be loaded from \"{Directory}\", so {LastResort} is being used. " +
            "Text may be laid out differently from the tested design.");
    }
}
