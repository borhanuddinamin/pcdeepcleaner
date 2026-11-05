using System.Globalization;
using System.Text.Json;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.CleanupEngine.Validation;

namespace WCM.CleanupEngine.Quarantine;

public sealed class QuarantineService : IQuarantineService
{
    public const string DeleteConfirmation = "DELETE";
    private static readonly TimeSpan MaxPlanAge = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly ICleanupRepository _repository;
    private readonly IAuditRepository _audit;
    private readonly CleanupValidator _validator;
    private readonly IDataDirectoryService _directories;
    private readonly Func<string, string, bool> _sameVolume;

    public QuarantineService(ICleanupRepository repository, IAuditRepository audit, CleanupValidator validator, IDataDirectoryService directories)
        : this(repository, audit, validator, directories, SameRoot)
    {
    }

    internal QuarantineService(ICleanupRepository repository, IAuditRepository audit, CleanupValidator validator, IDataDirectoryService directories, Func<string, string, bool> sameVolume)
    {
        _repository = repository;
        _audit = audit;
        _validator = validator;
        _directories = directories;
        _sameVolume = sameVolume;
    }

    private string QuarantineRoot => Path.Combine(_directories.DataRoot, "Quarantine");

    public Task<OperationResult> ExecuteAsync(long planId, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => ExecuteCoreAsync(planId, progress, cancellationToken), CancellationToken.None);

    public Task<IReadOnlyList<QuarantineItemView>> GetItemsAsync(CancellationToken cancellationToken) =>
        Task.Run(() => _repository.GetItemsAsync(cancellationToken), CancellationToken.None);

    public Task<OperationResult> RestoreAsync(IReadOnlyList<long> itemIds, string conflictMode, CancellationToken cancellationToken) =>
        Task.Run(() => RestoreCoreAsync(itemIds, conflictMode, cancellationToken), CancellationToken.None);

    public Task<OperationResult> DeletePermanentlyAsync(IReadOnlyList<long> itemIds, string confirmation, CancellationToken cancellationToken) =>
        Task.Run(() => DeleteCoreAsync(itemIds, confirmation, cancellationToken), CancellationToken.None);

    public Task<OperationResult> RecoverAsync(CancellationToken cancellationToken) =>
        Task.Run(() => RecoverCoreAsync(cancellationToken), CancellationToken.None);

    private static bool SameRoot(string a, string b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    private async Task<OperationResult> ExecuteCoreAsync(long planId, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var plan = await _repository.GetPlanAsync(planId, cancellationToken)
            ?? throw new InvalidOperationException("The plan doesn't exist.");
        if (plan.Status != PlanStatus.Approved || plan.ApprovedAt is null)
        {
            throw new InvalidOperationException("This plan isn't approved, or it has already run.");
        }

        var approvedAt = DateTimeOffset.Parse(plan.ApprovedAt, CultureInfo.InvariantCulture);
        if (DateTimeOffset.UtcNow - approvedAt > MaxPlanAge)
        {
            throw new InvalidOperationException("This plan is too old. Create a new preview.");
        }

        if (!await _repository.ClaimPlanAsync(planId, cancellationToken))
        {
            throw new InvalidOperationException("This plan has already run.");
        }

        var now = DateTimeOffset.UtcNow;
        await _audit.AppendAsync(new AuditEntry(now, "QuarantineStarted", null, "InProgress", $"Plan {planId}"), cancellationToken);
        var operationId = await _repository.CreateOperationAsync(planId, now, cancellationToken);
        var outcomes = new List<ItemOutcome>();

        foreach (var item in (await _repository.GetPlanItemsAsync(planId, cancellationToken)).Where(item => item.IsValid))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                outcomes.Add(new ItemOutcome(0, item.Path, ItemState.Failed, "Cancelled before this item started."));
                continue;
            }

            progress?.Report(item.Path);
            outcomes.Add(await QuarantineItemAsync(operationId, item, cancellationToken));
        }

        var failed = outcomes.Any(outcome => outcome.Status != ItemState.Quarantined);
        await _repository.SetOperationStatusAsync(operationId, failed ? "CompletedWithProblems" : "Completed", CancellationToken.None);
        await _repository.SetPlanStatusAsync(planId, PlanStatus.Executed, CancellationToken.None);
        await WriteManifestAsync(operationId);
        return new OperationResult(operationId, outcomes);
    }

    private async Task<ItemOutcome> QuarantineItemAsync(long operationId, PlanItem item, CancellationToken cancellationToken)
    {
        var token = CancellationToken.None;
        var itemId = await _repository.AddQuarantineItemAsync(operationId, item, string.Empty, DateTimeOffset.UtcNow, token);
        var destination = Path.Combine(QuarantineRoot, $"op-{operationId}", itemId.ToString(CultureInfo.InvariantCulture), "payload");
        await _repository.SetQuarantinePathAsync(itemId, destination, token);

        try
        {
            var outcome = await _validator.ValidateAsync(new CleanupSelection(item.Path, item.SourceKind, item.AppKey, item.RuleId), cancellationToken);
            if (!outcome.IsValid)
            {
                return await FinishAsync(itemId, item.Path, ItemState.Failed, "Blocked by the final check: " + string.Join(" ", outcome.BlockReasons));
            }

            if (outcome.Stats?.Fingerprint != item.Fingerprint)
            {
                return await FinishAsync(itemId, item.Path, ItemState.Failed, "The item changed after the preview.");
            }

            string hash;
            if (_sameVolume(item.Path, destination))
            {
                hash = FileOperations.HashTree(item.Path, cancellationToken);
                FileOperations.Move(item.Path, destination);
            }
            else
            {
                hash = CopyVerifyRemove(item.Path, destination, item.SizeBytes, cancellationToken);
            }

            if (!FileOperations.HashTree(destination, token).Equals(hash, StringComparison.Ordinal))
            {
                return await FinishAsync(itemId, item.Path, ItemState.VerificationFailed, "The quarantined copy doesn't match. Both locations were kept as they are.");
            }

            await _repository.SetItemStateAsync(itemId, ItemState.Quarantined, null, hash, token);
            await _audit.AppendAsync(new AuditEntry(DateTimeOffset.UtcNow, "Quarantine", item.Path, ItemState.Quarantined, destination), token);
            return new ItemOutcome(itemId, item.Path, ItemState.Quarantined, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
        {
            var state = FileOperations.Exists(destination) && FileOperations.Exists(item.Path) ? ItemState.RecoveryRequired : ItemState.Failed;
            return await FinishAsync(itemId, item.Path, state, ex.Message);
        }
    }

    // A cross-volume move isn't atomic, so the original stays until a verified copy exists.
    private string CopyVerifyRemove(string source, string destination, long bytes, CancellationToken cancellationToken)
    {
        var problem = FileOperations.FindCopyProblem(source);
        if (problem is not null)
        {
            throw new IOException(problem);
        }

        var root = Path.GetPathRoot(destination)!;
        if (new DriveInfo(root).AvailableFreeSpace < bytes)
        {
            throw new IOException("The quarantine drive doesn't have enough free space.");
        }

        var staging = destination + ".staging";
        var sourceHash = FileOperations.HashTree(source, cancellationToken);
        FileOperations.CopyTree(source, staging, cancellationToken);
        if (!FileOperations.HashTree(staging, cancellationToken).Equals(sourceHash, StringComparison.Ordinal))
        {
            throw new IOException("The copy doesn't match the original, so the original was kept.");
        }

        FileOperations.Move(staging, destination);
        FileOperations.RemoveOriginalAfterVerifiedCopy(source, sourceHash, cancellationToken);
        return sourceHash;
    }

    private async Task<ItemOutcome> FinishAsync(long itemId, string path, string state, string message)
    {
        await _repository.SetItemStateAsync(itemId, state, message, null, CancellationToken.None);
        await _audit.AppendAsync(new AuditEntry(DateTimeOffset.UtcNow, "Quarantine", path, state, message), CancellationToken.None);
        return new ItemOutcome(itemId, path, state, message);
    }

    private async Task WriteManifestAsync(long operationId)
    {
        var folder = Path.Combine(QuarantineRoot, $"op-{operationId}");
        if (!Directory.Exists(folder))
        {
            return;
        }

        var items = (await _repository.GetItemsAsync(CancellationToken.None)).Where(item => item.OperationId == operationId);
        var manifest = Path.Combine(folder, "manifest.json");
        if (!File.Exists(manifest))
        {
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(items, Indented));
        }
    }

    private async Task<OperationResult> RestoreCoreAsync(IReadOnlyList<long> itemIds, string conflictMode, CancellationToken cancellationToken)
    {
        if (conflictMode is not (RestoreConflictMode.Stop or RestoreConflictMode.KeepBoth))
        {
            throw new ArgumentException("The conflict choice isn't recognized.", nameof(conflictMode));
        }

        var outcomes = new List<ItemOutcome>();
        foreach (var id in itemIds)
        {
            var item = await _repository.GetItemAsync(id, cancellationToken);
            if (item is null || item.Status != ItemState.Quarantined)
            {
                outcomes.Add(new ItemOutcome(id, item?.OriginalPath ?? string.Empty, ItemState.Failed, "Only quarantined items can be restored."));
                continue;
            }

            outcomes.Add(await RestoreItemAsync(item, conflictMode, cancellationToken));
        }

        return new OperationResult(null, outcomes);
    }

    private async Task<ItemOutcome> RestoreItemAsync(QuarantineItemView item, string conflictMode, CancellationToken cancellationToken)
    {
        var token = CancellationToken.None;
        var target = item.OriginalPath;
        if (FileOperations.Exists(target))
        {
            if (conflictMode == RestoreConflictMode.Stop)
            {
                await _repository.RecordRestoreAsync(item.Id, "Conflict", null, DateTimeOffset.UtcNow, token);
                return new ItemOutcome(item.Id, target, "Conflict", "Something already exists at the original location. Nothing was overwritten.");
            }

            target = FreeName(target);
        }

        try
        {
            if (!FileOperations.Exists(item.QuarantinePath))
            {
                await _repository.SetItemStateAsync(item.Id, ItemState.RecoveryRequired, "The quarantined item is missing.", null, token);
                return new ItemOutcome(item.Id, target, ItemState.RecoveryRequired, "The quarantined item is missing.");
            }

            if (item.VerificationHash is not null && !FileOperations.HashTree(item.QuarantinePath, cancellationToken).Equals(item.VerificationHash, StringComparison.Ordinal))
            {
                await _repository.SetItemStateAsync(item.Id, ItemState.VerificationFailed, "The quarantined content has changed, so it wasn't restored.", null, token);
                return new ItemOutcome(item.Id, target, ItemState.VerificationFailed, "The quarantined content has changed, so it wasn't restored.");
            }

            await _repository.SetItemStateAsync(item.Id, ItemState.Restoring, null, null, token);
            if (_sameVolume(item.QuarantinePath, target))
            {
                FileOperations.Move(item.QuarantinePath, target);
            }
            else
            {
                FileOperations.CopyTree(item.QuarantinePath, target, cancellationToken);
                var copied = FileOperations.HashTree(target, token);
                if (copied != item.VerificationHash)
                {
                    await _repository.SetItemStateAsync(item.Id, ItemState.RecoveryRequired, "The restored copy doesn't match. Both copies were kept.", null, token);
                    return new ItemOutcome(item.Id, target, ItemState.RecoveryRequired, "The restored copy doesn't match. Both copies were kept.");
                }

                FileOperations.DeleteInsideQuarantine(item.QuarantinePath, QuarantineRoot);
            }

            if (item.VerificationHash is not null && !FileOperations.HashTree(target, token).Equals(item.VerificationHash, StringComparison.Ordinal))
            {
                await _repository.SetItemStateAsync(item.Id, ItemState.RecoveryRequired, "The restored content doesn't match.", null, token);
                return new ItemOutcome(item.Id, target, ItemState.RecoveryRequired, "The restored content doesn't match.");
            }

            await _repository.SetRestoredPathAsync(item.Id, target, token);
            await _repository.SetItemStateAsync(item.Id, ItemState.Restored, null, null, token);
            await _repository.RecordRestoreAsync(item.Id, ItemState.Restored, target, DateTimeOffset.UtcNow, token);
            await _audit.AppendAsync(new AuditEntry(DateTimeOffset.UtcNow, "Restore", target, ItemState.Restored, item.QuarantinePath), token);
            return new ItemOutcome(item.Id, target, ItemState.Restored, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
        {
            await _repository.SetItemStateAsync(item.Id, ItemState.RecoveryRequired, ex.Message, null, token);
            await _repository.RecordRestoreAsync(item.Id, ItemState.RecoveryRequired, null, DateTimeOffset.UtcNow, token);
            return new ItemOutcome(item.Id, target, ItemState.RecoveryRequired, ex.Message);
        }
    }

    private static string FreeName(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var number = 1; ; number++)
        {
            var candidate = Path.Combine(folder, $"{name} (restored {number}){extension}");
            if (!FileOperations.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private async Task<OperationResult> DeleteCoreAsync(IReadOnlyList<long> itemIds, string confirmation, CancellationToken cancellationToken)
    {
        if (!string.Equals(confirmation, DeleteConfirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Permanent deletion wasn't confirmed.");
        }

        var token = CancellationToken.None;
        var outcomes = new List<ItemOutcome>();
        foreach (var id in itemIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = await _repository.GetItemAsync(id, token);
            if (item is null || item.Status != ItemState.Quarantined)
            {
                outcomes.Add(new ItemOutcome(id, item?.OriginalPath ?? string.Empty, ItemState.Failed, "Only quarantined items can be deleted."));
                continue;
            }

            try
            {
                await _repository.SetItemStateAsync(id, ItemState.Deleting, null, null, token);
                FileOperations.DeleteInsideQuarantine(item.QuarantinePath, QuarantineRoot);
                await _repository.SetItemStateAsync(id, ItemState.PermanentlyDeleted, null, null, token);
                await _repository.RecordDeletionAsync(id, ItemState.PermanentlyDeleted, item.SizeBytes, DateTimeOffset.UtcNow, token);
                await _audit.AppendAsync(new AuditEntry(DateTimeOffset.UtcNow, "PermanentDeletion", item.QuarantinePath, ItemState.PermanentlyDeleted, item.OriginalPath), token);
                outcomes.Add(new ItemOutcome(id, item.OriginalPath, ItemState.PermanentlyDeleted, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                var state = FileOperations.Exists(item.QuarantinePath) ? ItemState.RecoveryRequired : ItemState.Failed;
                await _repository.SetItemStateAsync(id, state, ex.Message, null, token);
                await _repository.RecordDeletionAsync(id, ItemState.Failed, 0, DateTimeOffset.UtcNow, token);
                outcomes.Add(new ItemOutcome(id, item.OriginalPath, state, ex.Message));
            }
        }

        return new OperationResult(null, outcomes);
    }

    private async Task<OperationResult> RecoverCoreAsync(CancellationToken cancellationToken)
    {
        var outcomes = new List<ItemOutcome>();
        foreach (var item in await _repository.GetIncompleteItemsAsync(cancellationToken))
        {
            var original = FileOperations.Exists(item.OriginalPath) || (item.RestoredPath is not null && FileOperations.Exists(item.RestoredPath));
            var quarantined = item.QuarantinePath.Length > 0 && FileOperations.Exists(item.QuarantinePath);
            var staging = item.QuarantinePath.Length > 0 && FileOperations.Exists(item.QuarantinePath + ".staging");

            var (state, message) = item.Status switch
            {
                _ when original && (quarantined || staging) => (ItemState.RecoveryRequired, "Copies exist in both places. Both were kept for you to review."),
                _ when !original && !quarantined => (ItemState.RecoveryRequired, "The item wasn't found in either location."),
                ItemState.Deleting when quarantined => (ItemState.Quarantined, "Deletion didn't finish, so the item stays in quarantine."),
                ItemState.Restoring when original => (ItemState.Restored, "The restore had finished."),
                ItemState.Restoring => (ItemState.Quarantined, "The restore didn't finish, so the item stays in quarantine."),
                _ when quarantined => (ItemState.Quarantined, "The move had finished."),
                _ => (ItemState.Failed, "The move hadn't started, so the original wasn't changed."),
            };

            await _repository.SetItemStateAsync(item.Id, state, message, null, cancellationToken);
            await _audit.AppendAsync(new AuditEntry(DateTimeOffset.UtcNow, "Recovery", item.OriginalPath, state, message), cancellationToken);
            outcomes.Add(new ItemOutcome(item.Id, item.OriginalPath, state, message));
        }

        return new OperationResult(null, outcomes);
    }
}
