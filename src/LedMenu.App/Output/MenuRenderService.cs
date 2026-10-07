using System.IO;
using System.Text.Json;
using LedMenu.Core.Logging;
using LedMenu.Core.Menus;
using LedMenu.Persistence;
using LedMenu.Rendering;

namespace LedMenu.App.Output;

/// <summary>
/// The one place the app turns a menu into pages. The LED output and the operator preview both ask here, so they
/// always show the same pixels. Results are kept until the menu's content, the screen size or the logo file changes,
/// so editing a menu (hide, show, price, anything) is reflected on the very next request. UI thread only.
/// </summary>
public sealed class MenuRenderService
{
    private readonly FontCatalog _fonts;
    private readonly AssetStore _assets;
    private readonly IAppLog _log;
    private readonly Dictionary<(Guid, int, int), (string Signature, MenuRenderResult Result)> _cache = new();

    public MenuRenderService(FontCatalog fonts, AssetStore assets, IAppLog log)
    {
        _fonts = fonts;
        _assets = assets;
        _log = log;
    }

    public FontCatalog Fonts => _fonts;

    public MenuRenderResult Get(Menu menu, int width, int height)
    {
        var logoPath = _assets.Resolve(menu.LogoAsset);
        var signature = JsonSerializer.Serialize(menu) + "|" + (logoPath == null ? "nologo" : "logo:" + LogoStamp(logoPath));
        var key = (menu.Id, width, height);
        if (_cache.TryGetValue(key, out var hit) && hit.Signature == signature) return hit.Result;

        var result = MenuRenderer.Render(menu, width, height, _fonts, logoPath);
        _cache[key] = (signature, result);

        _log.Info($"Menu rendered: \"{menu.Name}\" {width}x{height}: {result.PageCount} page(s), {result.Problems.Count} problem(s).");
        foreach (var p in result.Problems) _log.Warn($"Menu \"{menu.Name}\": {p.Message}");
        return result;
    }

    public void Forget(Guid menuId)
    {
        foreach (var k in _cache.Keys.Where(k => k.Item1 == menuId).ToList()) _cache.Remove(k);
    }

    private static string LogoStamp(string path)
    {
        try { var f = new FileInfo(path); return f.Length + ":" + f.LastWriteTimeUtc.Ticks; }
        catch { return "?"; }
    }
}
