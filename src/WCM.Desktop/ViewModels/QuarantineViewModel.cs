using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Desktop.Converters;
using WCM.Desktop.Services;

namespace WCM.Desktop.ViewModels;

public sealed class QuarantineRow(QuarantineItemView item) : SelectableRow
{
    public QuarantineItemView Item { get; } = item;
}

public sealed class QuarantineViewModel : PageViewModel
{
    private const string DeleteWord = "DELETE";
    private readonly IQuarantineService _quarantine;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private IReadOnlyList<QuarantineRow> _items = [];

    public QuarantineViewModel(IQuarantineService quarantine, IDialogService dialogs, ISettingsStore settings)
        : base("Quarantine & Restore", "\uE72E")
    {
        _quarantine = quarantine;
        _dialogs = dialogs;
        _settings = settings;
        RestoreCommand = new AsyncRelayCommand(() => RestoreAsync(RestoreConflictMode.Stop));
        RestoreBesideCommand = new AsyncRelayCommand(() => RestoreAsync(RestoreConflictMode.KeepBoth));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        RecoverCommand = new AsyncRelayCommand(RecoverAsync);
        SelectAllCommand = new RelayCommand(() => SetSelected(Items.Where(row => row.Item.CanRestore), true));
        ClearSelectionCommand = new RelayCommand(() => SetSelected(Items, false));
    }

    public IRelayCommand SelectAllCommand { get; }

    public IRelayCommand ClearSelectionCommand { get; }

    public string SelectionSummary
    {
        get
        {
            var chosen = Chosen();
            return $"{chosen.Count:N0} selected, {BytesConverter.Format(chosen.Sum(row => row.Item.SizeBytes))}";
        }
    }

    private static void SetSelected(IEnumerable<QuarantineRow> rows, bool selected)
    {
        foreach (var row in rows)
        {
            row.IsSelected = selected;
        }
    }

    public IAsyncRelayCommand RestoreCommand { get; }

    public IAsyncRelayCommand RestoreBesideCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public IAsyncRelayCommand RecoverCommand { get; }

    public IReadOnlyList<QuarantineRow> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    public override Task RefreshAsync() => RunAsync(string.Empty, async () =>
    {
        await LoadAsync();
        Status = await RetentionReminderAsync();
    });

    // A reminder only. Retention never deletes anything.
    private async Task<string> RetentionReminderAsync()
    {
        var days = int.TryParse(await _settings.GetAsync("RetentionDays", CancellationToken.None), out var saved) ? saved : 30;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var old = Items.Count(row => row.Item.CanDelete
            && DateTimeOffset.TryParse(row.Item.CreatedAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var created)
            && created < cutoff);
        return old == 0 ? string.Empty : $"{old:N0} items have been in quarantine for more than {days} days. Review whether to restore or delete them.";
    }

    private async Task LoadAsync()
    {
        Items = (await _quarantine.GetItemsAsync(CancellationToken.None)).Select(item => new QuarantineRow(item)).ToList();
        foreach (var row in Items)
        {
            row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectionSummary));
        }

        OnPropertyChanged(nameof(SelectionSummary));
    }

    private List<QuarantineRow> Chosen() => Items.Where(row => row.IsSelected).ToList();

    private Task RestoreAsync(string mode) => RunAsync("Restoring...", async () =>
    {
        var chosen = Chosen();
        if (chosen.Count == 0)
        {
            Status = "Select at least one item first.";
            return;
        }

        var result = await _quarantine.RestoreAsync(chosen.Select(row => row.Item.Id).ToList(), mode, CancellationToken.None);
        await LoadAsync();
        Report(result, ItemState.Restored, "restored");
    });

    public async Task DeleteAsync()
    {
        var chosen = Chosen();
        if (chosen.Count == 0)
        {
            Status = "Select at least one item first.";
            return;
        }

        var bytes = chosen.Sum(row => row.Item.SizeBytes);
        if (!_dialogs.ConfirmByTyping(
            "Delete permanently?",
            $"{chosen.Count:N0} quarantined items ({BytesConverter.Format(bytes)}) will be deleted. This can't be undone, and they can't be restored afterward.",
            DeleteWord))
        {
            Status = "Nothing was deleted.";
            return;
        }

        await RunAsync("Deleting...", async () =>
        {
            var result = await _quarantine.DeletePermanentlyAsync(chosen.Select(row => row.Item.Id).ToList(), DeleteWord, CancellationToken.None);
            await LoadAsync();
            Report(result, ItemState.PermanentlyDeleted, "deleted");
        });
    }

    private Task RecoverAsync() => RunAsync("Checking interrupted items...", async () =>
    {
        var result = await _quarantine.RecoverAsync(CancellationToken.None);
        await LoadAsync();
        Status = result.Items.Count == 0 ? "No interrupted items." : $"Checked {result.Items.Count:N0} interrupted items. No files were changed.";
    });

    private void Report(OperationResult result, string success, string verb)
    {
        var problems = result.Items.Where(item => item.Status != success).ToList();
        Status = $"{result.Succeeded(success):N0} items {verb}.";
        if (problems.Count > 0)
        {
            Problem = $"{problems.Count:N0} items weren't {verb}. {problems[0].Path}: {problems[0].Message}";
        }
    }
}
