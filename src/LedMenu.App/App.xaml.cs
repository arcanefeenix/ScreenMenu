using System.Threading;
using System.Windows;
using System.Windows.Interop;
using LedMenu.App.Display;
using LedMenu.App.Output;
using LedMenu.App.ViewModels;
using LedMenu.Core.Display;
using LedMenu.Core.Logging;
using LedMenu.Core.Menus;
using LedMenu.Core.Models;
using LedMenu.Core.Screens;
using LedMenu.Persistence;
using LedMenu.Rendering;
using System.IO;

namespace LedMenu.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private FileLog? _log;
    private JsonFileStore<AppSettings>? _settingsStore;
    private AppSettings _settings = new();
    private JsonFileStore<ScreenLayout>? _screensStore;
    private ScreenLayout _screenLayout = new();
    private MenuLibrary? _menuLibrary;
    private DisplaysViewModel? _displays;
    private OutputController? _controller;
    private StopHotKey? _hotKey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // If the operator window closes, the app must end; a leftover fullscreen output window would strand the operator.
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        _singleInstance = new Mutex(true, @"Local\LedMenuControl.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("LED Menu Control is already running.", "LED Menu Control",
                MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance = null;
            Shutdown();
            return;
        }

        var paths = AppPaths.Default();
        paths.EnsureCreated();
        _log = new FileLog(paths.Logs);
        InstallExceptionHandlers();

        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        _log.Info($"Application startup. Version {version}. Data folder {paths.Root}");

        _settingsStore = new JsonFileStore<AppSettings>(
            paths.SettingsFile, paths.Backups, () => new AppSettings(), AppSettings.Validate, _log);
        var load = _settingsStore.Load();
        _settings = load.Value;
        _log.Info($"Settings loaded: {load.Status}");

        _settings.LastStartUtc = DateTime.UtcNow;
        try { _settingsStore.Save(_settings); }
        catch (Exception ex) { _log.Error("Could not save settings at startup.", ex); }

        var notices = new List<string>();
        if (load.Status is LoadStatus.RecoveredFromBackup or LoadStatus.DefaultedAfterFailure && load.Message != null)
            notices.Add(load.Message);

        _screensStore = new JsonFileStore<ScreenLayout>(
            paths.ScreensFile, paths.Backups, () => new ScreenLayout(), ScreenLayout.ValidateFile, _log);
        var screensLoad = _screensStore.Load();
        _screenLayout = screensLoad.Value;
        _log.Info($"Screen layout loaded: {screensLoad.Status}, {_screenLayout.Screens.Count} screen(s)");
        if (screensLoad.Status is LoadStatus.RecoveredFromBackup or LoadStatus.DefaultedAfterFailure && screensLoad.Message != null)
            notices.Add("Screens: " + screensLoad.Message);
        var assets = new AssetStore(paths.Assets, _log);
        _menuLibrary = new MenuLibrary(new FileMenuStore(paths, _log), _log);
        _menuLibrary.Load();
        foreach (var issue in _menuLibrary.LoadIssues) notices.Add("Menus: " + issue.Message);
        var notice = notices.Count > 0 ? string.Join("\n", notices) : null;

        // Diagnostic mode: LedMenu.App.exe --dump-calibration <folder> [WIDTHxHEIGHT]
        var dumpIdx = Array.IndexOf(e.Args, "--dump-calibration");
        if (dumpIdx >= 0 && dumpIdx + 1 < e.Args.Length)
        {
            int dw = 1920, dh = 1080;
            if (dumpIdx + 2 < e.Args.Length)
            {
                var parts = e.Args[dumpIdx + 2].ToLowerInvariant().Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out var pw) && int.TryParse(parts[1], out var ph)) { dw = pw; dh = ph; }
            }
            PatternDump.Write(e.Args[dumpIdx + 1], dw, dh, _screenLayout);
            _log.Info($"Calibration patterns written to {e.Args[dumpIdx + 1]} for {dw}x{dh}.");
            Shutdown();
            return;
        }

        // Diagnostic mode: LedMenu.App.exe --dump-menus <folder> [logo.png] [WIDTHxHEIGHT]
        var menuDumpIdx = Array.IndexOf(e.Args, "--dump-menus");
        if (menuDumpIdx >= 0 && menuDumpIdx + 1 < e.Args.Length)
        {
            // optional extra arguments, in any order: an existing image file (logo) and/or a size like 168x672
            string? logo = null;
            (int, int)? size = null;
            foreach (var a in e.Args.Skip(menuDumpIdx + 2))
            {
                if (File.Exists(a)) { logo = a; continue; }
                var parts = a.ToLowerInvariant().Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out var sw) && int.TryParse(parts[1], out var sh)) size = (sw, sh);
            }
            var count = PatternDump.WriteMenus(e.Args[menuDumpIdx + 1],
                _menuLibrary!.Menus.Count > 0 ? _menuLibrary.Menus : SampleMenus.All.Select(i => i.Create()).ToList(), _screenLayout,
                new FontCatalog(Path.Combine(AppContext.BaseDirectory, "Fonts")), assets, logo, _log, size);
            _log.Info($"Menu pages written to {e.Args[menuDumpIdx + 1]}: {count} file(s).");
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // Built after Show() so the operator window's current monitor is known to the display list.
        _displays = new DisplaysViewModel(
            new Win32DisplaySource(_log), _settings, SaveSettings, _log,
            confirm: ConfirmSelection,
            operatorWindowMonitor: () => Win32DisplaySource.MonitorOfWindow(new WindowInteropHelper(window).Handle),
            identify: (display, number) => new IdentifyWindow(display, number, TimeSpan.FromSeconds(4)).Show());

        var hwnd = new WindowInteropHelper(window).Handle;
        _controller = new OutputController(_log);
        _hotKey = new StopHotKey(hwnd, _log);

        var fonts = new FontCatalog(Path.Combine(AppContext.BaseDirectory, "Fonts"));
        _log.Info($"Fonts folder: {fonts.Directory}; families: {string.Join(", ", fonts.Families)}");
        var render = new MenuRenderService(fonts, assets, _log);
        var menus = new MenusViewModel(_menuLibrary, assets, _log, PickImage, ConfirmDeleteMenu, render);
        var screens = new ScreensViewModel(
            _screenLayout, SaveScreens, _log,
            canvasProvider: () => OutputCanvasResolver.Resolve(_displays.OutputMatch, _settings.OutputDisplay),
            confirmRemove: ConfirmRemoveScreen,
            menus: menus);
        _displays.Refreshed += screens.RefreshCanvas;   // output size may change; screens are flagged, never edited
        var output = new OutputViewModel(_controller, _displays, screens, menus, render, _hotKey, _log, ConfirmStart);
        menus.LivePageOf = output.LivePageOf;
        menus.ScreenSizeFor = id => screens.ScreenSizeForMenu(id);
        window.DataContext = new MainViewModel(paths.Root, version, notice, _displays, output, screens, menus);

        // Diagnostic mode: LedMenu.App.exe --selftest-output <report file>
        var args = e.Args;
        var idx = Array.IndexOf(args, "--selftest-output");
        if (idx >= 0 && idx + 1 < args.Length)
        {
            var report = args[idx + 1];
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await OutputSelfTest.RunAsync(output, _controller, paths.SettingsFile, hwnd, report, _log);
                Shutdown();
            });
        }
    }

    private string? PickImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a logo image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private bool ConfirmDeleteMenu(string text, string title) =>
        MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void SaveScreens() => _screensStore?.Save(_screenLayout);   // errors are reported by the screens view model

    private bool ConfirmRemoveScreen(string name) =>
        MessageBox.Show($"Remove \"{name}\" from the screen layout?", "Remove screen",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void SaveSettings()
    {
        try { _settingsStore?.Save(_settings); }
        catch (Exception ex) { _log?.Error("Could not save settings.", ex); }
    }

    private bool ConfirmSelection(IReadOnlyList<string> warnings, string question) =>
        MessageBox.Show(
            string.Join("\n\n", warnings.Select(w => "• " + w)) + "\n\n" + question,
            "Confirm display selection", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    private bool ConfirmStart(IReadOnlyList<string> warnings, string question) =>
        MessageBox.Show(
            string.Join("\n\n", warnings.Select(w => "• " + w)) + "\n\n" + question,
            "Confirm LED output", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _hotKey?.Dispose();
        _displays?.Dispose();
        _log?.Info("Application shutdown.");
        if (_singleInstance != null)
        {
            try { _singleInstance.ReleaseMutex(); } catch { }
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    /// <summary>
    /// An unhandled UI exception is logged and swallowed so the operator keeps control;
    /// non-UI-thread failures are logged before the runtime tears the process down.
    /// </summary>
    private void InstallExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _log?.Error("Unhandled UI exception.", args.Exception);
            args.Handled = true;
            MessageBox.Show("Something went wrong, but the application is still running.\n\n" + args.Exception.Message,
                "LED Menu Control", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _log?.Error("Fatal unhandled exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log?.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }
}
