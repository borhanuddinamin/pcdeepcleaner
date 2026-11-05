using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Desktop.Converters;
using WCM.Desktop.Services;

namespace WCM.Desktop.ViewModels;

public sealed class CleanupCenterViewModel : PageViewModel
{
    private readonly ICleanupPlanner _planner;
    private readonly IQuarantineService _quarantine;
    private readonly CleanupBasket _basket;
    private readonly IDialogService _dialogs;
    private CleanupPreview? _preview;
    private bool _isApproved;
    private bool _userAcknowledged;
    private IReadOnlyList<CleanupSelection> _selections = [];

    public CleanupCenterViewModel(ICleanupPlanner planner, IQuarantineService quarantine, CleanupBasket basket, IDialogService dialogs)
        : base("Cleanup Center", "\uE74D")
    {
        _planner = planner;
        _quarantine = quarantine;
        _basket = basket;
        _dialogs = dialogs;
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => Selections.Count > 0);
        ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => UserAcknowledged && !_isApproved && (Preview?.Plan.SelectedItems ?? 0) > 0);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => _isApproved);
        ClearCommand = new RelayCommand(Clear);
    }

#pragma warning disable CS0067 // Reserved for pages that open this one.
    public event EventHandler? OpenRequested;
#pragma warning restore CS0067

    public IAsyncRelayCommand PreviewCommand { get; }

    public IAsyncRelayCommand ApproveCommand { get; }

    public IAsyncRelayCommand ExecuteCommand { get; }

    public IRelayCommand ClearCommand { get; }

    public IReadOnlyList<CleanupSelection> Selections
    {
        get => _selections;
        private set => SetProperty(ref _selections, value);
    }

    public CleanupPreview? Preview
    {
        get => _preview;
        private set
        {
            if (SetProperty(ref _preview, value))
            {
                OnPropertyChanged(nameof(Summary));
                ApproveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Summary => Preview is null
        ? "Create a preview to see exactly what would move. A preview changes no files."
        : $"{Preview.Plan.SelectedItems:N0} of {Preview.Items.Count:N0} items passed, {BytesConverter.Format(Preview.Plan.EstimatedBytes)}. Destination: {Preview.QuarantineRoot}";

    public bool UserAcknowledged
    {
        get => _userAcknowledged;
        set
        {
            if (SetProperty(ref _userAcknowledged, value))
            {
                ApproveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public override Task RefreshAsync()
    {
        Selections = _basket.Items;
        PreviewCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }

    private void Clear()
    {
        _basket.Clear();
        Selections = [];
        Preview = null;
        SetApproved(false);
        UserAcknowledged = false;
        PreviewCommand.NotifyCanExecuteChanged();
        Status = string.Empty;
    }

    private void SetApproved(bool approved)
    {
        _isApproved = approved;
        ApproveCommand.NotifyCanExecuteChanged();
        ExecuteCommand.NotifyCanExecuteChanged();
    }

    private Task PreviewAsync() => RunAsync("Checking every selected item...", async () =>
    {
        SetApproved(false);
        UserAcknowledged = false;
        Preview = await _planner.CreatePlanAsync(Selections, CancellationToken.None);
        Status = "Preview ready. No files were changed.";
    });

    private Task ApproveAsync() => RunAsync("Saving approval...", async () =>
    {
        var approved = await _planner.ApproveAsync(Preview!.Plan.Id, CancellationToken.None);
        SetApproved(approved);
        Status = approved ? "Plan approved. Every item is checked again just before it moves." : "This plan can't be approved. Create a new preview.";
    });

    private async Task ExecuteAsync()
    {
        var plan = Preview!.Plan;
        if (!_dialogs.Confirm(
            "Move to quarantine?",
            $"{plan.SelectedItems:N0} items ({BytesConverter.Format(plan.EstimatedBytes)}) will move to quarantine. You can restore them. On the same drive, this doesn't free space until you delete them permanently.",
            "Move to quarantine"))
        {
            return;
        }

        await RunAsync("Moving to quarantine...", async () =>
        {
            SetApproved(false);
            var progress = new Progress<string>(path => Status = $"Moving {path}");
            var result = await _quarantine.ExecuteAsync(plan.Id, progress, CancellationToken.None);
            var moved = result.Succeeded(ItemState.Quarantined);
            var problems = result.Items.Where(item => item.Status != ItemState.Quarantined).ToList();
            Status = $"{moved:N0} items moved to quarantine.";
            if (problems.Count > 0)
            {
                Problem = $"{problems.Count:N0} items didn't move. {problems[0].Path}: {problems[0].Message}";
            }

            _basket.Clear();
            Selections = [];
            Preview = null;
            UserAcknowledged = false;
        });
    }
}
