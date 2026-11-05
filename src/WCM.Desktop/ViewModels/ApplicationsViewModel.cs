using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;

namespace WCM.Desktop.ViewModels;

public sealed class LocationRowViewModel(LocationMapping mapping, Func<long, string, Task> decide)
{
    public LocationMapping Mapping { get; } = mapping;

    public string EvidenceText { get; } = string.Join(
        Environment.NewLine,
        mapping.Evidence.Select(item => $"{item.Score:+0;-0;0}  {item.Description}"));

    public IAsyncRelayCommand ConfirmCommand { get; } = new AsyncRelayCommand(() => decide(mapping.MappingId, UserDecision.Confirmed));

    public IAsyncRelayCommand RejectCommand { get; } = new AsyncRelayCommand(() => decide(mapping.MappingId, UserDecision.Rejected));
}

public sealed class ApplicationsViewModel : PageViewModel
{
    private readonly IApplicationDiscoveryService _discovery;
    private readonly IApplicationRepository _applications;
    private readonly IOwnershipService _ownership;
    private IReadOnlyList<ApplicationRecord> _items = [];
    private IReadOnlyList<LocationRowViewModel> _locations = [];
    private ApplicationRecord? _selected;
    private string _status = "Select Scan to list the applications on this PC. Scanning only reads information.";

    public ApplicationsViewModel(
        IApplicationDiscoveryService discovery,
        IApplicationRepository applications,
        IOwnershipService ownership)
    {
        Title = "Applications";
        _discovery = discovery;
        _applications = applications;
        _ownership = ownership;
        ScanCommand = new AsyncRelayCommand(ScanAsync);
        MapCommand = new AsyncRelayCommand(MapAsync);
    }

    public override string Title { get; }

    public IAsyncRelayCommand ScanCommand { get; }

    public IAsyncRelayCommand MapCommand { get; }

    public IReadOnlyList<ApplicationRecord> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    public IReadOnlyList<LocationRowViewModel> Locations
    {
        get => _locations;
        private set => SetProperty(ref _locations, value);
    }

    public ApplicationRecord? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                _ = LoadLocationsAsync();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public async Task LoadLocationsAsync()
    {
        if (_selected is null)
        {
            Locations = [];
            return;
        }

        try
        {
            var mappings = await _ownership.GetLocationsAsync(_selected.AppKey, CancellationToken.None);
            Locations = mappings.Select(mapping => new LocationRowViewModel(mapping, DecideAsync)).ToList();
        }
        catch (Exception ex)
        {
            Status = $"Couldn't load this application's locations: {ex.Message}";
        }
    }

    private async Task DecideAsync(long mappingId, string decision)
    {
        try
        {
            await _ownership.SetDecisionAsync(mappingId, decision, CancellationToken.None);
            Status = "Saved. Your decision doesn't remove the location's protection.";
            await LoadLocationsAsync();
        }
        catch (Exception ex)
        {
            Status = $"Couldn't save your decision: {ex.Message}";
        }
    }

    private async Task MapAsync(CancellationToken cancellationToken)
    {
        Status = "Mapping storage...";
        try
        {
            var result = await _ownership.MapAsync(cancellationToken);
            Status = result.WasSaved
                ? $"Mapped {result.Locations:N0} locations: {result.SharedLocations:N0} shared and {result.UnknownLocations:N0} with an unknown owner. All stay protected."
                : result.Problem ?? "Mapping didn't finish.";
            await LoadLocationsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = $"Mapping failed: {ex.Message}";
        }
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
