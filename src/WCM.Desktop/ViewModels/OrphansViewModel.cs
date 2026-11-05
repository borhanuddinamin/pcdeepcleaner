using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;

namespace WCM.Desktop.ViewModels;

public sealed class OrphansViewModel : PageViewModel
{
    private const string RuleId = "orphan-reviewed";
    private readonly IOrphanService _orphans;
    private readonly CleanupBasket _basket;
    private List<OrphanCandidate> _all = [];
    private IReadOnlyList<OrphanCandidate> _items = [];
    private OrphanCandidate? _selected;
    private string _search = string.Empty;
    private string _filter = "All";

    public OrphansViewModel(IOrphanService orphans, CleanupBasket basket)
        : base("Orphaned Data", "\uE7BA")
    {
        _orphans = orphans;
        _basket = basket;
        DetectCommand = new AsyncRelayCommand(DetectAsync);
        ApproveCommand = new AsyncRelayCommand(() => DecideAsync(CandidateDecision.Approved), () => Selected is not null);
        KeepCommand = new AsyncRelayCommand(() => DecideAsync(CandidateDecision.Kept), () => Selected is not null);
        ResetCommand = new AsyncRelayCommand(() => DecideAsync(CandidateDecision.Pending), () => Selected is not null);
        SendCommand = new RelayCommand(Send);
    }

    public event EventHandler? OpenCleanupRequested;

    public IReadOnlyList<string> Filters { get; } = ["All", "Confirmed by history", "Potential", "Unconfirmed", "Approved", "Kept"];

    public IAsyncRelayCommand DetectCommand { get; }

    public IAsyncRelayCommand ApproveCommand { get; }

    public IAsyncRelayCommand KeepCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public IRelayCommand SendCommand { get; }

    public IReadOnlyList<OrphanCandidate> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    public OrphanCandidate? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                ApproveCommand.NotifyCanExecuteChanged();
                KeepCommand.NotifyCanExecuteChanged();
                ResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value))
            {
                Apply();
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
                Apply();
            }
        }
    }

    public override Task RefreshAsync() => RunAsync(string.Empty, async () =>
    {
        _all = (await _orphans.GetAsync(CancellationToken.None)).ToList();
        Apply();
    });

    private void Apply()
    {
        Items = _all.Where(item =>
            (_search.Length == 0 || item.Path.Contains(_search, StringComparison.OrdinalIgnoreCase))
            && _filter switch
            {
                "Confirmed by history" => item.Classification == OrphanClassification.ConfirmedByHistory,
                "Potential" => item.Classification == OrphanClassification.Potential,
                "Unconfirmed" => item.Classification == OrphanClassification.Unconfirmed,
                "Approved" => item.Decision == CandidateDecision.Approved,
                "Kept" => item.Decision == CandidateDecision.Kept,
                _ => true,
            }).ToList();
    }

    private Task DetectAsync(CancellationToken cancellationToken) => RunAsync("Looking for leftovers...", async () =>
    {
        _all = (await _orphans.DetectAsync(cancellationToken)).ToList();
        Apply();
        Status = $"Found {_all.Count:N0} candidates. None is approved until you approve it.";
    });

    private Task DecideAsync(string decision) => RunAsync("Saving...", async () =>
    {
        await _orphans.SetDecisionAsync(Selected!.Id, decision, CancellationToken.None);
        _all = (await _orphans.GetAsync(CancellationToken.None)).ToList();
        Apply();
        Status = $"Marked as {decision.ToLowerInvariant()}.";
    });

    private void Send()
    {
        var approved = _all.Where(item => item.Decision == CandidateDecision.Approved).ToList();
        if (approved.Count == 0)
        {
            Status = "Approve at least one candidate first.";
            return;
        }

        foreach (var item in approved)
        {
            _basket.Add(new CleanupSelection(item.Path, SourceKind.Orphan, item.AppKey, RuleId));
        }

        OpenCleanupRequested?.Invoke(this, EventArgs.Empty);
    }
}
