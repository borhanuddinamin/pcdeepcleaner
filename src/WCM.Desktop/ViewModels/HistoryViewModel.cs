using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Desktop.Services;

namespace WCM.Desktop.ViewModels;

public sealed class HistoryViewModel : PageViewModel
{
    private readonly IDashboardQuery _query;
    private readonly IAuditRepository _audit;
    private readonly IReportService _reports;
    private readonly IDialogService _dialogs;
    private IReadOnlyList<ScanHistoryRow> _sessions = [];
    private IReadOnlyList<ScanErrorRow> _errors = [];
    private IReadOnlyList<AuditEntry> _auditEntries = [];
    private ScanHistoryRow? _selected;
    private string _report = ReportKind.Applications;
    private string _format = "CSV";

    public HistoryViewModel(IDashboardQuery query, IAuditRepository audit, IReportService reports, IDialogService dialogs)
        : base("Scan History", "\uE81C")
    {
        _query = query;
        _audit = audit;
        _reports = reports;
        _dialogs = dialogs;
        ExportCommand = new AsyncRelayCommand(ExportAsync);
    }

    public IReadOnlyList<string> ReportKinds => ReportKind.All;

    public IReadOnlyList<string> Formats { get; } = ["CSV", "JSON"];

    public IAsyncRelayCommand ExportCommand { get; }

    public IReadOnlyList<ScanHistoryRow> Sessions
    {
        get => _sessions;
        private set => SetProperty(ref _sessions, value);
    }

    public IReadOnlyList<ScanErrorRow> Errors
    {
        get => _errors;
        private set => SetProperty(ref _errors, value);
    }

    public IReadOnlyList<AuditEntry> AuditEntries
    {
        get => _auditEntries;
        private set => SetProperty(ref _auditEntries, value);
    }

    public string Report
    {
        get => _report;
        set => SetProperty(ref _report, value);
    }

    public string Format
    {
        get => _format;
        set => SetProperty(ref _format, value);
    }

    public ScanHistoryRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
            {
                _ = RunAsync(string.Empty, async () => Errors = await _query.GetScanErrorsAsync(value.Id, CancellationToken.None));
            }
        }
    }

    public override Task RefreshAsync() => RunAsync(string.Empty, async () =>
    {
        Sessions = await _query.GetScanHistoryAsync(CancellationToken.None);
        AuditEntries = await _audit.GetRecentAsync(200, CancellationToken.None);
    });

    private async Task ExportAsync()
    {
        var extension = Format.ToLowerInvariant();
        var path = _dialogs.PickSaveFile($"wcm-{Report}.{extension}", $"{Format} file|*.{extension}");
        if (path is not null)
        {
            await RunAsync("Exporting...", async () =>
            {
                var rows = await Task.Run(() => _reports.ExportAsync(Report, extension, path, CancellationToken.None));
                Status = $"Exported {rows:N0} rows to {path}";
            });
        }
    }
}
