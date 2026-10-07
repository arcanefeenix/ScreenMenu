using LedMenu.App.Infrastructure;

namespace LedMenu.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private string? _startupNotice;

    public MainViewModel(string dataFolder, string version, string? startupNotice, DisplaysViewModel displays)
    {
        DataFolder = dataFolder;
        Version = version;
        _startupNotice = startupNotice;
        Displays = displays;
        DismissNoticeCommand = new RelayCommand(() => StartupNotice = null);
    }

    public string DataFolder { get; }
    public string Version { get; }
    public DisplaysViewModel Displays { get; }
    public RelayCommand DismissNoticeCommand { get; }

    /// <summary>Banner text when startup recovered or reset data. Null when everything was normal.</summary>
    public string? StartupNotice
    {
        get => _startupNotice;
        set
        {
            if (Set(ref _startupNotice, value)) OnPropertyChanged(nameof(HasStartupNotice));
        }
    }

    public bool HasStartupNotice => !string.IsNullOrEmpty(_startupNotice);
}
