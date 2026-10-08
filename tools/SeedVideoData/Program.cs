using LedMenu.Core.Assets;
using LedMenu.Core.Calibration;
using LedMenu.Core.Logging;
using LedMenu.Core.Menus;
using LedMenu.Core.Models;
using LedMenu.Core.Screens;
using LedMenu.Persistence;

// Usage: SeedVideoData <dataDir> <clip1> [clip2 ...]
// Creates: menus\<dense menu>.json, media\<hash-named clips>, screens.json (screen 1 = 168x672 menu at 0,0; screen 2 = 168x672 video at 168,0).
// settings.json (which display is the LED output) is written separately by the driver script.
var dataDir = args[0];
var clips = args.Skip(1).ToArray();
var paths = new AppPaths(dataDir);
paths.EnsureCreated();

var menu = SampleMenus.DenseMenu();
new FileMenuStore(paths).Save(menu);

var store = new MediaStore(paths.Media, new FixedProbe());
var playlist = new VideoPlaylist { Loop = true };
foreach (var c in clips)
{
    var r = store.ImportAsync(c).GetAwaiter().GetResult();
    playlist.Items.Add(r.Item);
    Console.WriteLine($"imported {r.Item.DisplayName} -> {r.Item.FileName}  length {r.Item.DurationSeconds:0.000} s");
}

var layout = new ScreenLayout
{
    Screens =
    {
        new Screen { Name = "Menu", X = 0, Y = 0, Width = 168, Height = 672, AssignedMenuId = menu.Id },
        new Screen { Name = "Video", X = 168, Y = 0, Width = 168, Height = 672, ContentKind = ScreenContentKind.Video, Playlist = playlist },
    },
};
new JsonFileStore<ScreenLayout>(paths.ScreensFile, paths.Backups, () => new ScreenLayout(), ScreenLayout.ValidateFile).Save(layout);
Console.WriteLine($"seeded {dataDir}: menu \"{menu.Name}\", {playlist.Items.Count} video(s)");

internal sealed class FixedProbe : IVideoProbe
{
    public Task<VideoProbeResult> ProbeAsync(string path, CancellationToken cancel = default) =>
        Task.FromResult(new VideoProbeResult(true, 168, 672, 10, null));
}
