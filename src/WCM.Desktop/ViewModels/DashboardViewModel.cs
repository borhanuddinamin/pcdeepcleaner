using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Desktop.Converters;

namespace WCM.Desktop.ViewModels;

public sealed record StatCard(string Label, string Value, string Hint);

public sealed class DashboardViewModel : PageViewModel
{
    private readonly IDashboardQuery _query;
    private readonly IApplicationDiscoveryService _discovery;
    private readonly IStorageScanner _scanner;
    private readonly IScanConfigurationStore _settings;
    private readonly IOwnershipService _ownership;
    private readonly IOrphanService _orphans;
    private readonly ICacheAnalyzer _cache;
    private readonly IQuarantineService _quarantine;
    private IReadOnlyList<StatCard> _cards = [];
    private string _lastScan = "No scan has run yet.";
    private string _notice = string.Empty;
    private bool _recoveryChecked;

    public DashboardViewModel(
        IDashboardQuery query,
        IApplicationDiscoveryService discovery,
        IStorageScanner scanner,
        IScanConfigurationStore settings,
        IOwnershipService ownership,
        IOrphanService orphans,
        ICacheAnalyzer cache,
        IQuarantineService quarantine)
        : base("Dashboard", "\uE80F")
    {
        _query = query;
        _discovery = discovery;
        _scanner = scanner;
        _settings = settings;
        _ownership = ownership;
        _orphans = orphans;
        _cache = cache;
        _quarantine = quarantine;
        FullScanCommand = new AsyncRelayCommand(FullScanAsync);
        CancelCommand = FullScanCommand.CreateCancelCommand();
    }

    public IAsyncRelayCommand FullScanCommand { get; }

    public System.Windows.Input.ICommand CancelCommand { get; }

    public IReadOnlyList<StatCard> Cards
    {
        get => _cards;
        private set => SetProperty(ref _cards, value);
    }

    public string LastScan
    {
        get => _lastScan;
        private set => SetProperty(ref _lastScan, value);
    }

    public string Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    public override async Task RefreshAsync()
    {
        try
        {
            if (!_recoveryChecked)
            {
                _recoveryChecked = true;
                var recovered = await _quarantine.RecoverAsync(CancellationToken.None);
                if (recovered.Items.Count > 0)
                {
                    Notice = $"{recovered.Items.Count} interrupted items were checked. No files were changed. Review them in Quarantine & Restore.";
                }
            }

            await LoadCardsAsync();
        }
        catch (Exception ex)
        {
            Problem = ex.Message;
        }
    }

    private async Task LoadCardsAsync()
    {
        var stats = await _query.GetAsync(CancellationToken.None);
        var cache = await _cache.AnalyzeAsync(CancellationToken.None);
        Cards =
        [
            new("Applications detected", $"{stats.InstalledApplications:N0}", "Their data stays protected."),
            new("Leftover candidates", $"{stats.OrphanCandidates:N0}", "Nothing here is approved until you review it."),
            new("Cache candidates", $"{cache.Count:N0}", "Only folders named by a local rule."),
            new("Discovered", BytesConverter.Format(stats.DiscoveredBytes), "All data measured by the last finished scan."),
            new("Protected", BytesConverter.Format(stats.ProtectedBytes), "Linked to detected applications."),
            new("Candidates", BytesConverter.Format(stats.CandidateBytes + cache.Sum(item => item.SizeBytes)), "Not the same as recoverable."),
            new("Validated and approved", BytesConverter.Format(stats.EligibleBytes), "Passed checks in an approved plan."),
            new("In quarantine", BytesConverter.Format(stats.QuarantinedBytes), "Still uses disk space on the same drive."),
            new("Space recovered", BytesConverter.Format(stats.FreedBytes), "Only permanent deletion frees space."),
        ];
        LastScan = stats.LastScanStatus is null
            ? "No storage scan has run yet."
            : $"Last storage scan: {HumanizeConverter.Format(stats.LastScanStatus).ToLowerInvariant()}, with {stats.LastScanErrors:N0} unreadable items.";
    }

    private Task FullScanAsync(CancellationToken cancellationToken) => RunAsync("Finding applications...", async () =>
    {
        var discovered = await _discovery.DiscoverAsync(cancellationToken);
        Status = "Scanning storage...";
        var configuration = await _settings.LoadAsync(cancellationToken);
        var progress = new Progress<ScanProgress>(p => Status = $"Scanning storage: {p.Files:N0} files, {BytesConverter.Format(p.Bytes)}, {p.Errors:N0} unreadable");
        var scan = await _scanner.ScanAsync(configuration, progress, cancellationToken);
        if (scan.Status is ScanStatus.Cancelled or ScanStatus.Failed)
        {
            Status = StorageViewModel.Summarize(scan);
            await LoadCardsAsync();
            return;
        }

        Status = "Linking folders to applications...";
        await _ownership.MapAsync(cancellationToken);
        Status = "Looking for leftovers...";
        var leftovers = await _orphans.DetectAsync(cancellationToken);
        await LoadCardsAsync();
        var incomplete = discovered.IsComplete && scan.IsComplete ? string.Empty : " Some items couldn't be read, so totals are incomplete.";
        Status = $"Done: {discovered.ApplicationCount:N0} applications, {scan.Files:N0} files, {leftovers.Count:N0} leftover candidates. Nothing was changed.{incomplete}";
    });
}
