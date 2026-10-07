using LedMenu.App.Infrastructure;

namespace LedMenu.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private string _outputStatus = "OUTPUT: STOPPED";
    private string? _startupNotice;

    public MainViewModel(string dataFolder, string version, string? startupNotice)
    {
        DataFolder = dataFolder;
        Version = version;
        _startupNotice = startupNotice;
        DismissNoticeCommand = new RelayCommand(() => StartupNotice = null);
    }

    public string DataFolder { get; }
    public string Version { get; }
    public RelayCommand DismissNoticeCommand { get; }

    public string OutputStatus
    {
        get => _outputStatus;
        set => Set(ref _outputStatus, value);
    }

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
