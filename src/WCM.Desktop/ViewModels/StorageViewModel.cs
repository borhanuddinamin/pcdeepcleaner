using System.IO;
using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Desktop.Converters;

namespace WCM.Desktop.ViewModels;

public sealed class StorageViewModel : PageViewModel
{
    private readonly IStorageScanner _scanner;
    private readonly IScanConfigurationStore _settings;
    private readonly IDashboardQuery _query;
    private string _currentPath = string.Empty;
    private IReadOnlyList<string> _notScanned = [];
    private IReadOnlyList<FolderRow> _folders = [];
    private string? _parent;
    private string _search = string.Empty;
    private FolderRow? _selected;

    public StorageViewModel(IStorageScanner scanner, IScanConfigurationStore settings, IDashboardQuery query)
        : base("Storage Explorer", "\uE8B7")
    {
        _scanner = scanner;
        _settings = settings;
        _query = query;
        ScanCommand = new AsyncRelayCommand(ScanAsync);
        CancelCommand = ScanCommand.CreateCancelCommand();
        OpenCommand = new AsyncRelayCommand(OpenAsync);
        UpCommand = new AsyncRelayCommand(UpAsync);
        SearchCommand = new AsyncRelayCommand(LoadAsync);
    }

    public IAsyncRelayCommand ScanCommand { get; }

    public System.Windows.Input.ICommand CancelCommand { get; }

    public IAsyncRelayCommand OpenCommand { get; }

    public IAsyncRelayCommand UpCommand { get; }

    public IAsyncRelayCommand SearchCommand { get; }

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    public string Location => _parent ?? "Mapped locations";

    public IReadOnlyList<string> NotScanned
    {
        get => _notScanned;
        private set => SetProperty(ref _notScanned, value);
    }

    public IReadOnlyList<FolderRow> Folders
    {
        get => _folders;
        private set => SetProperty(ref _folders, value);
    }

    public string Search
    {
        get => _search;
        set => SetProperty(ref _search, value);
    }

    public FolderRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public static string Summarize(ScanResult result)
    {
        var counts = $"{result.Files:N0} files in {result.Folders:N0} folders, {BytesConverter.Format(result.Bytes)}";
        var skipped = $"{result.LinksSkipped:N0} links not followed.";
        return result.Status switch
        {
            ScanStatus.Completed => $"Scan complete: {counts}. {skipped}",
            ScanStatus.CompletedWithErrors => $"Scan finished, but {result.Errors:N0} items couldn't be read, so these totals are incomplete: {counts}. {skipped}",
            ScanStatus.Cancelled => $"Scan cancelled. These totals are incomplete: {counts}.",
            _ => $"Scan failed: {result.FailureReason}",
        };
    }

    public override Task RefreshAsync() => RunAsync(string.Empty, async () =>
    {
        var configuration = await _settings.LoadAsync(CancellationToken.None);
        NotScanned =
        [
            .. configuration.Exclusions.Select(e => $"{e.Path}: {e.Reason}"),
            .. configuration.Roots.Where(r => !r.Enabled).Select(r => $"{r.Path}: {r.Policy}"),
        ];
        await LoadAsync();
    });

    private async Task LoadAsync()
    {
        Folders = await Task.Run(() => _query.GetFoldersAsync(_parent, _search, CancellationToken.None));
        OnPropertyChanged(nameof(Location));
    }

    private async Task OpenAsync()
    {
        if (Selected is { ItemType: ScanItemType.Folder } folder)
        {
            _parent = folder.Path;
            Search = string.Empty;
            await LoadAsync();
        }
    }

    private async Task UpAsync()
    {
        _parent = _parent is null ? null : Path.GetDirectoryName(_parent);
        await LoadAsync();
    }

    private Task ScanAsync(CancellationToken cancellationToken) => RunAsync("Scanning...", async () =>
    {
        var configuration = await _settings.LoadAsync(cancellationToken);
        var progress = new Progress<ScanProgress>(p =>
        {
            Status = $"Scanning: {p.Files:N0} files, {p.Folders:N0} folders, {BytesConverter.Format(p.Bytes)}, {p.Errors:N0} unreadable";
            CurrentPath = p.CurrentPath ?? string.Empty;
        });
        var result = await _scanner.ScanAsync(configuration, progress, cancellationToken);
        Status = Summarize(result);
        CurrentPath = string.Empty;
        await LoadAsync();
    });
}
