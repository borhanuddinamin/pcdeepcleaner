using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;

namespace WCM.Desktop.ViewModels;

public sealed class StorageViewModel : PageViewModel
{
    private readonly IStorageScanner _scanner;
    private readonly IScanConfigurationStore _settings;
    private string _status = "Select Scan storage to measure the configured folders. Scanning only reads file information.";
    private string _currentPath = string.Empty;
    private IReadOnlyList<string> _notScanned = [];

    public StorageViewModel(IStorageScanner scanner, IScanConfigurationStore settings)
        : base("Storage Explorer")
    {
        _scanner = scanner;
        _settings = settings;
        ScanCommand = new AsyncRelayCommand(ScanAsync);
        CancelCommand = ScanCommand.CreateCancelCommand();
    }

    public IAsyncRelayCommand ScanCommand { get; }

    public System.Windows.Input.ICommand CancelCommand { get; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    public IReadOnlyList<string> NotScanned
    {
        get => _notScanned;
        private set => SetProperty(ref _notScanned, value);
    }

    public static string Summarize(ScanResult result)
    {
        var counts = $"{result.Files:N0} files in {result.Folders:N0} folders, {FormatBytes(result.Bytes)}";
        var skipped = $"{result.LinksSkipped:N0} links not followed.";
        return result.Status switch
        {
            ScanStatus.Completed => $"Scan complete: {counts}. {skipped}",
            ScanStatus.CompletedWithErrors => $"Scan finished, but {result.Errors:N0} items couldn't be read, so these totals are incomplete: {counts}. {skipped}",
            ScanStatus.Cancelled => $"Scan cancelled. These totals are incomplete: {counts}.",
            _ => $"Scan failed: {result.FailureReason}",
        };
    }

    private static string FormatBytes(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):N1} GB"
        : $"{bytes / (double)(1L << 20):N1} MB";

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        Status = "Scanning...";
        try
        {
            var configuration = await _settings.LoadAsync(cancellationToken);
            NotScanned =
            [
                .. configuration.Exclusions.Select(e => $"{e.Path}: {e.Reason}"),
                .. configuration.Roots.Where(r => !r.Enabled).Select(r => $"{r.Path}: {r.Policy}"),
            ];
            var progress = new Progress<ScanProgress>(p =>
            {
                Status = $"Scanning: {p.Files:N0} files, {p.Folders:N0} folders, {FormatBytes(p.Bytes)}, {p.Errors:N0} unreadable";
                CurrentPath = p.CurrentPath ?? string.Empty;
            });

            var result = await _scanner.ScanAsync(configuration, progress, cancellationToken);
            Status = Summarize(result);
            CurrentPath = string.Empty;
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled before it started.";
        }
        catch (Exception ex)
        {
            Status = $"Scan failed: {ex.Message}";
        }
    }
}
