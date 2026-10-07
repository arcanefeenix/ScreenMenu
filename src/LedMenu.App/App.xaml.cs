using System.Threading;
using System.Windows;
using LedMenu.App.ViewModels;
using LedMenu.Core.Logging;
using LedMenu.Core.Models;
using LedMenu.Persistence;

namespace LedMenu.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private FileLog? _log;
    private JsonFileStore<AppSettings>? _settingsStore;
    private AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        var notice = load.Status is LoadStatus.RecoveredFromBackup or LoadStatus.DefaultedAfterFailure
            ? load.Message : null;

        MainWindow = new MainWindow { DataContext = new MainViewModel(paths.Root, version, notice) };
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
