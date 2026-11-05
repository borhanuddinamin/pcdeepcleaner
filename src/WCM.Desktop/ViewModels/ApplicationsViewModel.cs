using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;

namespace WCM.Desktop.ViewModels;

public sealed class ApplicationsViewModel : PageViewModel
{
    private readonly IApplicationDiscoveryService _discovery;
    private readonly IApplicationRepository _applications;
    private IReadOnlyList<ApplicationRecord> _items = [];
    private string _status = "Select Scan to list the applications on this PC. Scanning only reads information.";

    public ApplicationsViewModel(IApplicationDiscoveryService discovery, IApplicationRepository applications)
        : base("Applications")
    {
        _discovery = discovery;
        _applications = applications;
        ScanCommand = new AsyncRelayCommand(ScanAsync);
    }

    public IAsyncRelayCommand ScanCommand { get; }

    public IReadOnlyList<ApplicationRecord> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        Status = "Scanning...";
        try
        {
            var result = await _discovery.DiscoverAsync(cancellationToken);
            Items = await _applications.GetAllAsync(cancellationToken);
            Status = result switch
            {
                { WasSaved: false } => $"Scan failed, and the saved list wasn't changed. {string.Join(" ", result.Failures)}",
                { IsComplete: false } => $"Found {result.ApplicationCount} applications, but the scan was incomplete. {string.Join(" ", result.Failures)}",
                _ => $"Found {result.ApplicationCount} applications.",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = $"Scan failed: {ex.Message}";
        }
    }
}
