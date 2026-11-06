using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;

namespace WCM.Desktop.ViewModels;

public sealed class CacheViewModel : PageViewModel
{
    private readonly ICacheAnalyzer _analyzer;
    private readonly CleanupBasket _basket;
    private List<SelectableCache> _all = [];
    private IReadOnlyList<SelectableCache> _items = [];
    private string _filter = "All";

    public CacheViewModel(ICacheAnalyzer analyzer, CleanupBasket basket)
        : base("Cache Analyzer", "\uEDA2")
    {
        _analyzer = analyzer;
        _basket = basket;
        SendCommand = new RelayCommand(Send);
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? OpenCleanupRequested;

    public event EventHandler? OpenSettingsRequested;

    public IRelayCommand OpenSettingsCommand { get; }

    public bool NothingSelectable => _all.Count > 0 && _all.All(item => !item.Candidate.IsEligible);

    public IReadOnlyList<string> Filters { get; } =
        ["All", "Application cache", "Temporary files", "Updater data", "Logs and crash dumps", "Package caches", "Build caches"];

    public IRelayCommand SendCommand { get; }

    public IReadOnlyList<SelectableCache> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
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

    // Nothing is ever selected automatically.
    public override Task RefreshAsync() => RunAsync(string.Empty, async () =>
    {
        _all = (await _analyzer.AnalyzeAsync(CancellationToken.None)).Select(item => new SelectableCache(item)).ToList();
        OnPropertyChanged(nameof(NothingSelectable));
        Apply();
    });

    private void Apply() => Items = _all.Where(item => _filter == "All" || item.Candidate.Category == _filter).ToList();

    private void Send()
    {
        var chosen = _all.Where(item => item.IsSelected).ToList();
        if (chosen.Count == 0)
        {
            Status = "Select at least one item first.";
            return;
        }

        foreach (var item in chosen)
        {
            _basket.Add(new CleanupSelection(item.Candidate.Path, SourceKind.Cache, item.Candidate.ApplicationKey, item.Candidate.RuleId));
        }

        OpenCleanupRequested?.Invoke(this, EventArgs.Empty);
    }
}
