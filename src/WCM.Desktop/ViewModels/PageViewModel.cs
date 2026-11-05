using CommunityToolkit.Mvvm.ComponentModel;

namespace WCM.Desktop.ViewModels;

public abstract class PageViewModel(string title, string icon) : ObservableObject
{
    private string _status = string.Empty;
    private string _problem = string.Empty;
    private bool _isBusy;

    public string Title { get; } = title;

    public string Icon { get; } = icon;

    public string Status
    {
        get => _status;
        protected set => SetProperty(ref _status, value);
    }

    /// <summary>Shown in a banner, so failures aren't mistaken for empty results.</summary>
    public string Problem
    {
        get => _problem;
        protected set => SetProperty(ref _problem, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Loads saved data each time the page opens. It never starts a scan.</summary>
    public virtual Task RefreshAsync() => Task.CompletedTask;

    protected async Task RunAsync(string busyText, Func<Task> action)
    {
        IsBusy = true;
        Status = busyText;
        Problem = string.Empty;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
        }
        catch (Exception ex)
        {
            Status = string.Empty;
            Problem = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class MainWindowViewModel : ObservableObject
{
    private PageViewModel _selectedPage;

    public MainWindowViewModel(
        DashboardViewModel dashboard,
        ApplicationsViewModel applications,
        OrphansViewModel orphans,
        CacheViewModel cache,
        StorageViewModel storage,
        CleanupCenterViewModel cleanup,
        QuarantineViewModel quarantine,
        HistoryViewModel history,
        SettingsViewModel settings)
    {
        Pages = [dashboard, applications, orphans, cache, storage, cleanup, quarantine, history, settings];
        _selectedPage = settings.IsFirstRun ? settings : dashboard;
        cleanup.OpenRequested += (_, _) => SelectedPage = cleanup;
        orphans.OpenCleanupRequested += (_, _) => SelectedPage = cleanup;
        cache.OpenCleanupRequested += (_, _) => SelectedPage = cleanup;
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    public Task StartAsync() => _selectedPage.RefreshAsync();

    public PageViewModel SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value) && value is not null)
            {
                _ = value.RefreshAsync();
            }
        }
    }
}
