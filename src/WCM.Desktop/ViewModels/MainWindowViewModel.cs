using CommunityToolkit.Mvvm.ComponentModel;

namespace WCM.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private PageViewModel _selectedPage;

    public MainWindowViewModel(SettingsViewModel settings, ApplicationsViewModel applications)
    {
        Pages =
        [
            new PlaceholderPageViewModel("Dashboard"),
            applications,
            new PlaceholderPageViewModel("Orphaned Data"),
            new PlaceholderPageViewModel("Cache Analyzer"),
            new PlaceholderPageViewModel("Storage Explorer"),
            new PlaceholderPageViewModel("Cleanup Center"),
            new PlaceholderPageViewModel("Quarantine & Restore"),
            new PlaceholderPageViewModel("Scan History"),
            settings,
        ];
        _selectedPage = settings.IsFirstRun ? settings : Pages[0];
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    public PageViewModel SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value);
    }
}
