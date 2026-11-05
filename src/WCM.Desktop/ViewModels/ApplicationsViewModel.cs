using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Enums;

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
    public const string FilterAll = "All";
    private readonly IApplicationDiscoveryService _discovery;
    private readonly IApplicationRepository _applications;
    private readonly IOwnershipService _ownership;
    private List<ApplicationRecord> _all = [];
    private IReadOnlyList<LocationRowViewModel> _locations = [];
    private ICollectionView _view = CollectionViewSource.GetDefaultView(new List<ApplicationRecord>());
    private ApplicationRecord? _selected;
    private string _search = string.Empty;
    private string _filter = FilterAll;
    private int _count;

    public ApplicationsViewModel(IApplicationDiscoveryService discovery, IApplicationRepository applications, IOwnershipService ownership)
        : base("Applications", "\uE71D")
    {
        _discovery = discovery;
        _applications = applications;
        _ownership = ownership;
        ScanCommand = new AsyncRelayCommand(ScanAsync);
        MapCommand = new AsyncRelayCommand(MapAsync);
    }

    public IReadOnlyList<string> Filters { get; } =
        [FilterAll, "Installed", "Potentially uninstalled", "Portable", "Unknown", "Needs review"];

    public IAsyncRelayCommand ScanCommand { get; }

    public IAsyncRelayCommand MapCommand { get; }

    public ICollectionView Items
    {
        get => _view;
        private set => SetProperty(ref _view, value);
    }

    public int VisibleCount
    {
        get => _count;
        private set => SetProperty(ref _count, value);
    }

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value))
            {
                ApplyFilter();
            }
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                ApplyFilter();
            }
        }
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

    public override Task RefreshAsync() => RunAsync(string.Empty, LoadAsync);

    public bool Matches(ApplicationRecord item)
    {
        var filterOk = _filter switch
        {
            "Installed" => item.Status == ApplicationStatus.Installed,
            "Potentially uninstalled" => item.Status == ApplicationStatus.NotDetected,
            "Portable" => item.Status == ApplicationStatus.Portable,
            "Unknown" => item.Status == ApplicationStatus.Unverified,
            "Needs review" => item.Status is ApplicationStatus.NeedsReview or ApplicationStatus.InstallPathMissing,
            _ => true,
        };
        return filterOk && (_search.Length == 0
            || item.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || (item.Publisher?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private void ApplyFilter()
    {
        Items.Filter = item => Matches((ApplicationRecord)item);
        VisibleCount = _all.Count(Matches);
    }

    private async Task LoadAsync()
    {
        _all = (await _applications.GetAllAsync(CancellationToken.None)).ToList();
        Items = new ListCollectionView(_all);
        ApplyFilter();
    }

    private async Task LoadLocationsAsync()
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
            Problem = $"Couldn't load this application's locations: {ex.Message}";
        }
    }

    private Task DecideAsync(long mappingId, string decision) => RunAsync("Saving...", async () =>
    {
        await _ownership.SetDecisionAsync(mappingId, decision, CancellationToken.None);
        Status = "Saved. Your decision doesn't remove the location's protection.";
        await LoadLocationsAsync();
    });

    private Task MapAsync(CancellationToken cancellationToken) => RunAsync("Mapping storage...", async () =>
    {
        var result = await _ownership.MapAsync(cancellationToken);
        Status = result.WasSaved
            ? $"Mapped {result.Locations:N0} locations: {result.SharedLocations:N0} shared and {result.UnknownLocations:N0} with an unknown owner. All stay protected."
            : result.Problem ?? "Mapping didn't finish.";
        await LoadLocationsAsync();
    });

    private Task ScanAsync(CancellationToken cancellationToken) => RunAsync("Scanning...", async () =>
    {
        var result = await _discovery.DiscoverAsync(cancellationToken);
        await LoadAsync();
        Status = result switch
        {
            { WasSaved: false } => "Scan failed, and the saved list wasn't changed.",
            { IsComplete: false } => $"Found {result.ApplicationCount} applications, but the scan was incomplete.",
            _ => $"Found {result.ApplicationCount} applications.",
        };
        if (result.Failures.Count > 0)
        {
            Problem = string.Join(" ", result.Failures);
        }
    });
}
