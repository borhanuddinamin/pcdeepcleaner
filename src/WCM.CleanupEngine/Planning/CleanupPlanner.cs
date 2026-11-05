using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.CleanupEngine.Validation;

namespace WCM.CleanupEngine.Planning;

/// <summary>Creates dry-run plans. It reads files and writes only plan rows to the database.</summary>
public sealed class CleanupPlanner(
    CleanupValidator validator,
    ICleanupRepository repository,
    IAuditRepository audit,
    IDataDirectoryService directories) : ICleanupPlanner
{
    public Task<CleanupPreview> CreatePlanAsync(IReadOnlyList<CleanupSelection> selections, CancellationToken cancellationToken) =>
        Task.Run(() => CreateCoreAsync(selections, cancellationToken), CancellationToken.None);

    public Task<bool> ApproveAsync(long planId, CancellationToken cancellationToken) => Task.Run(
        async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var approved = await repository.ApprovePlanAsync(planId, now, cancellationToken);
            await audit.AppendAsync(new AuditEntry(now, "PlanApproval", null, approved ? "Approved" : "Refused", $"Plan {planId}"), cancellationToken);
            return approved;
        },
        CancellationToken.None);

    private async Task<CleanupPreview> CreateCoreAsync(IReadOnlyList<CleanupSelection> selections, CancellationToken cancellationToken)
    {
        var items = new List<PlanItem>();
        var unsaved = new List<PlanItem>();
        foreach (var selection in selections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await validator.ValidateAsync(selection, cancellationToken);
            var scan = await repository.FindScanItemAsync(selection.Path, cancellationToken);
            var reasons = outcome.BlockReasons.ToList();
            if (scan is null)
            {
                reasons.Add("This item isn't in the latest scan.");
            }

            var item = new PlanItem
            {
                ScanItemId = scan?.Id ?? 0,
                Path = selection.Path,
                SourceKind = selection.SourceKind,
                AppKey = selection.AppKey,
                RuleId = selection.RuleId,
                SizeBytes = outcome.Stats?.Bytes ?? scan?.SizeBytes ?? 0,
                ItemCount = outcome.Stats?.Items ?? scan?.ItemCount ?? 0,
                Fingerprint = outcome.Stats?.Fingerprint,
                ValidationStatus = reasons.Count == 0 ? "Valid" : "Blocked",
                BlockReason = reasons.Count == 0 ? null : string.Join(" ", reasons),
            };
            (scan is null ? unsaved : items).Add(item);
        }

        var now = DateTimeOffset.UtcNow;
        var planId = await repository.CreatePlanAsync(items, now, cancellationToken);
        await audit.AppendAsync(new AuditEntry(now, "PlanCreated", null, "Planned", $"Plan {planId}: {items.Count(i => i.IsValid)} valid, {items.Count(i => !i.IsValid) + unsaved.Count} blocked"), cancellationToken);
        var saved = await repository.GetPlanItemsAsync(planId, cancellationToken);
        var plan = await repository.GetPlanAsync(planId, cancellationToken);
        return new CleanupPreview(plan!, [.. saved, .. unsaved], Path.Combine(directories.DataRoot, "Quarantine"));
    }
}
