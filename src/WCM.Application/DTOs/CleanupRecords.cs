namespace WCM.Application.DTOs;

public static class OrphanClassification
{
    public const string ConfirmedByHistory = "ConfirmedOrphanByHistory";
    public const string Potential = "PotentialOrphan";

    /// <summary>No history supports this. It is shown for review and stays unknown until you decide.</summary>
    public const string Unconfirmed = "UnconfirmedCandidate";
}

public static class CandidateDecision
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Kept = "Kept";
}

public static class SourceKind
{
    public const string Orphan = "Orphan";
    public const string Cache = "Cache";
}

public static class PlanStatus
{
    public const string Planned = "Planned";
    public const string Approved = "Approved";
    public const string InProgress = "InProgress";
    public const string Executed = "Executed";
}

public static class ItemState
{
    public const string InProgress = "InProgress";
    public const string Quarantined = "Quarantined";
    public const string VerificationFailed = "VerificationFailed";
    public const string RecoveryRequired = "RecoveryRequired";
    public const string Restoring = "Restoring";
    public const string Restored = "Restored";
    public const string Deleting = "Deleting";
    public const string PermanentlyDeleted = "PermanentlyDeleted";
    public const string Failed = "Failed";
}

public static class RestoreConflictMode
{
    /// <summary>Leaves the item in quarantine and reports the conflict.</summary>
    public const string Stop = "Stop";

    /// <summary>Restores beside the existing item under a new name.</summary>
    public const string KeepBoth = "KeepBoth";
}

public sealed class OrphanCandidate
{
    public long Id { get; set; }

    public string? AppKey { get; set; }

    public string? ApplicationName { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Classification { get; set; } = string.Empty;

    public string Decision { get; set; } = CandidateDecision.Pending;

    public long SizeBytes { get; set; }

    public string? LastDetectedAt { get; set; }

    public string Evidence { get; set; } = string.Empty;
}

public sealed record OrphanSource(string AppKey, string Name, string? DetectionSource, string? InstallLocation, string LastDetectedAt, int InstalledSnapshots, string Path, long SizeBytes, string Evidence, bool HasStrongEvidence);

public sealed record UnknownFolder(string Path, long SizeBytes, string? LastModifiedAt);

public sealed record CleanupRule(
    string RuleId,
    string RuleVersion,
    string ApplicationKey,
    string Target,
    string Scope,
    string DataType,
    string Category,
    bool CleanupAllowed,
    bool RequiresApplicationClosed,
    IReadOnlyList<string> ProcessNames,
    int MinimumAgeDays,
    string RebuildCost,
    string SensitiveWarning,
    string ValidationMethod);

public sealed record CacheCandidate(
    string RuleId,
    string ApplicationKey,
    string Category,
    string DataType,
    string Path,
    long SizeBytes,
    long ItemCount,
    string? LastModifiedAt,
    bool RequiresApplicationClosed,
    string RebuildCost,
    string SensitiveWarning,
    bool RuleEnabled,
    bool IsEligible,
    string Reason);

public sealed record CleanupSelection(string Path, string SourceKind, string? AppKey, string RuleId);

public sealed record TreeStats(long Bytes, long Items, DateTimeOffset Newest)
{
    public string Fingerprint => $"{Items}|{Bytes}|{Newest.UtcTicks}";
}

public sealed record ValidationOutcome(IReadOnlyList<string> BlockReasons, TreeStats? Stats)
{
    public bool IsValid => BlockReasons.Count == 0;
}

public sealed class PlanItem
{
    public long Id { get; set; }

    public long CleanupPlanId { get; set; }

    public long ScanItemId { get; set; }

    public string Path { get; set; } = string.Empty;

    public string SourceKind { get; set; } = string.Empty;

    public string? AppKey { get; set; }

    public string RuleId { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public long ItemCount { get; set; }

    public string? Fingerprint { get; set; }

    public string ValidationStatus { get; set; } = string.Empty;

    public string? BlockReason { get; set; }

    public bool IsValid => ValidationStatus == "Valid";
}

public sealed class PlanSummary
{
    public long Id { get; set; }

    public string CreatedAt { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public long SelectedItems { get; set; }

    public long EstimatedBytes { get; set; }

    public string? ApprovedAt { get; set; }
}

public sealed record CleanupPreview(PlanSummary Plan, IReadOnlyList<PlanItem> Items, string QuarantineRoot);

public sealed class QuarantineItemView
{
    public long Id { get; set; }

    public long OperationId { get; set; }

    public string? AppKey { get; set; }

    public string OriginalPath { get; set; } = string.Empty;

    public string QuarantinePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public long ItemCount { get; set; }

    public string? VerificationHash { get; set; }

    public string Status { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;

    public string? Message { get; set; }

    public string? RestoredPath { get; set; }

    public bool CanRestore => Status == ItemState.Quarantined;

    public bool CanDelete => Status == ItemState.Quarantined;
}

public sealed record ItemOutcome(long ItemId, string Path, string Status, string? Message);

public sealed record OperationResult(long? OperationId, IReadOnlyList<ItemOutcome> Items)
{
    public int Succeeded(string status) => Items.Count(item => item.Status == status);
}

public sealed record OwnerRow(string State, string AppStatus, string Name, string Path);

public sealed record ScanItemRef(long Id, long SizeBytes, long ItemCount);

public sealed class DashboardStats
{
    public long InstalledApplications { get; set; }

    public long OrphanCandidates { get; set; }

    public long CacheCandidates { get; set; }

    public long DiscoveredBytes { get; set; }

    public long ProtectedBytes { get; set; }

    public long CandidateBytes { get; set; }

    public long EligibleBytes { get; set; }

    public long QuarantinedBytes { get; set; }

    public long FreedBytes { get; set; }

    public string? LastScanAt { get; set; }

    public string? LastScanStatus { get; set; }

    public long LastScanErrors { get; set; }
}

public sealed class ScanHistoryRow
{
    public long Id { get; set; }

    public string StartedAt { get; set; } = string.Empty;

    public string? CompletedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public long FilesScanned { get; set; }

    public long FoldersScanned { get; set; }

    public long Errors { get; set; }

    public long TotalBytes { get; set; }

    public long LinksSkipped { get; set; }
}

public sealed class FolderRow
{
    public string Path { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public long ItemCount { get; set; }

    public string? LastModifiedAt { get; set; }

    public string ItemType { get; set; } = string.Empty;

    public bool IsPartial { get; set; }

    public string? Owners { get; set; }

    public string? DataType { get; set; }

    public string? OwnershipStatus { get; set; }

    public long? Confidence { get; set; }
}

public sealed class ScanErrorRow
{
    public string Path { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public static class ReportKind
{
    public const string Applications = "Applications";
    public const string StorageScan = "StorageScan";
    public const string OwnershipEvidence = "OwnershipEvidence";
    public const string OrphanCandidates = "OrphanCandidates";
    public const string CleanupPreview = "CleanupPreview";
    public const string QuarantineHistory = "QuarantineHistory";
    public const string DeletionHistory = "DeletionHistory";
    public const string RecoveredSpace = "RecoveredSpace";

    public static readonly IReadOnlyList<string> All =
        [Applications, StorageScan, OwnershipEvidence, OrphanCandidates, CleanupPreview, QuarantineHistory, DeletionHistory, RecoveredSpace];
}
