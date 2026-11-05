using WCM.Application.DTOs;

namespace WCM.Application.Interfaces;

public interface IPathNormalizer
{
    string Normalize(string path);
}

public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}

public interface IRuleCatalog
{
    /// <summary>Loads the rules with your enabled choices applied. Invalid files fail closed.</summary>
    /// <exception cref="InvalidDataException">The catalogue is missing or invalid.</exception>
    Task<IReadOnlyList<CleanupRule>> LoadAsync(CancellationToken cancellationToken);

    Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken cancellationToken);
}

public interface IProcessInspector
{
    bool IsRunning(string processName);

    /// <returns>The name of a process running from inside this folder, or null.</returns>
    string? FindProcessInside(string path);
}

public interface ICleanupRepository
{
    Task<ScanItemRef?> FindScanItemAsync(string path, CancellationToken cancellationToken);

    Task<IReadOnlyList<OwnerRow>> GetOwnersAsync(string path, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrphanSource>> GetNotDetectedMappingsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<UnknownFolder>> GetUnknownFoldersAsync(CancellationToken cancellationToken);

    Task SaveOrphansAsync(IReadOnlyList<OrphanCandidate> candidates, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrphanCandidate>> GetOrphansAsync(CancellationToken cancellationToken);

    Task SetOrphanDecisionAsync(long id, string decision, CancellationToken cancellationToken);

    Task<string?> GetOrphanDecisionAsync(string path, CancellationToken cancellationToken);

    Task<IReadOnlyList<FolderRow>> GetScanItemsUnderAsync(string target, bool childrenOnly, CancellationToken cancellationToken);

    Task<long> CreatePlanAsync(IReadOnlyList<PlanItem> items, DateTimeOffset createdAt, CancellationToken cancellationToken);

    Task<PlanSummary?> GetPlanAsync(long planId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlanItem>> GetPlanItemsAsync(long planId, CancellationToken cancellationToken);

    Task<bool> ApprovePlanAsync(long planId, DateTimeOffset approvedAt, CancellationToken cancellationToken);

    /// <summary>Moves an approved plan to in progress exactly once, so a plan can't run twice.</summary>
    Task<bool> ClaimPlanAsync(long planId, CancellationToken cancellationToken);

    Task SetPlanStatusAsync(long planId, string status, CancellationToken cancellationToken);

    Task<long> CreateOperationAsync(long planId, DateTimeOffset createdAt, CancellationToken cancellationToken);

    Task SetOperationStatusAsync(long operationId, string status, CancellationToken cancellationToken);

    Task<long> AddQuarantineItemAsync(long operationId, PlanItem item, string quarantinePath, DateTimeOffset at, CancellationToken cancellationToken);

    Task SetQuarantinePathAsync(long itemId, string quarantinePath, CancellationToken cancellationToken);

    Task SetItemStateAsync(long itemId, string status, string? message, string? hash, CancellationToken cancellationToken);

    Task SetRestoredPathAsync(long itemId, string path, CancellationToken cancellationToken);

    Task<QuarantineItemView?> GetItemAsync(long itemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<QuarantineItemView>> GetItemsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<QuarantineItemView>> GetIncompleteItemsAsync(CancellationToken cancellationToken);

    Task RecordRestoreAsync(long itemId, string status, string? path, DateTimeOffset at, CancellationToken cancellationToken);

    Task RecordDeletionAsync(long itemId, string status, long bytes, DateTimeOffset at, CancellationToken cancellationToken);
}

public interface IOrphanService
{
    Task<IReadOnlyList<OrphanCandidate>> DetectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OrphanCandidate>> GetAsync(CancellationToken cancellationToken);

    Task SetDecisionAsync(long id, string decision, CancellationToken cancellationToken);
}

public interface ICacheAnalyzer
{
    Task<IReadOnlyList<CacheCandidate>> AnalyzeAsync(CancellationToken cancellationToken);
}

public interface ICleanupPlanner
{
    /// <summary>Read-only for files. Saves a plan that still needs your approval.</summary>
    Task<CleanupPreview> CreatePlanAsync(IReadOnlyList<CleanupSelection> selections, CancellationToken cancellationToken);

    Task<bool> ApproveAsync(long planId, CancellationToken cancellationToken);
}

public interface IQuarantineService
{
    Task<OperationResult> ExecuteAsync(long planId, IProgress<string>? progress, CancellationToken cancellationToken);

    Task<IReadOnlyList<QuarantineItemView>> GetItemsAsync(CancellationToken cancellationToken);

    Task<OperationResult> RestoreAsync(IReadOnlyList<long> itemIds, string conflictMode, CancellationToken cancellationToken);

    /// <param name="confirmation">Must be exactly DELETE.</param>
    Task<OperationResult> DeletePermanentlyAsync(IReadOnlyList<long> itemIds, string confirmation, CancellationToken cancellationToken);

    /// <summary>Works out interrupted items. It never deletes or overwrites anything.</summary>
    Task<OperationResult> RecoverAsync(CancellationToken cancellationToken);
}

public interface IReportService
{
    Task<int> ExportAsync(string kind, string format, string destinationPath, CancellationToken cancellationToken);
}

public interface IDashboardQuery
{
    Task<DashboardStats> GetAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ScanHistoryRow>> GetScanHistoryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<FolderRow>> GetFoldersAsync(string? parent, string? search, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScanErrorRow>> GetScanErrorsAsync(long sessionId, CancellationToken cancellationToken);
}
