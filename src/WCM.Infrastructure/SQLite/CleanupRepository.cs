using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Enums;

namespace WCM.Infrastructure.SQLite;

public sealed class CleanupRepository(SqliteConnectionFactory connections) : ICleanupRepository
{
    private const string LatestScan = "(SELECT MAX(Id) FROM ScanSessions WHERE Status IN ('Completed', 'CompletedWithErrors'))";
    private const string ItemColumns = "Id, OperationId, AppKey, OriginalPath, QuarantinePath, SizeBytes, ItemCount, VerificationHash, Status, COALESCE(CreatedAt, '') AS CreatedAt, Message, RestoredPath";

    public async Task<ScanItemRef?> FindScanItemAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var row = await connection.QueryFirstOrDefaultAsync<(long Id, long SizeBytes, long ItemCount)?>(new CommandDefinition(
            $"SELECT Id, SizeBytes, ItemCount FROM ScanItems WHERE ScanSessionId = {LatestScan} AND Path = @path",
            new { path },
            cancellationToken: cancellationToken));
        return row is null ? null : new ScanItemRef(row.Value.Id, row.Value.SizeBytes, row.Value.ItemCount);
    }

    // Ownership of the path itself, any parent or any child blocks an orphan.
    public async Task<IReadOnlyList<OwnerRow>> GetOwnersAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<(string State, string AppStatus, string Name, string Path)>(new CommandDefinition(
            """
            SELECT m.OwnershipStatus, a.Status, a.Name, s.Path
            FROM ApplicationStorageMappings m
            JOIN Applications a ON a.Id = m.ApplicationId
            JOIN StorageLocations s ON s.Id = m.StorageLocationId
            WHERE s.Path = @path
               OR lower(substr(@path, 1, length(s.Path) + 1)) = lower(s.Path || '\')
               OR lower(substr(s.Path, 1, length(@path) + 1)) = lower(@path || '\')
            """,
            new { path },
            cancellationToken: cancellationToken));
        return rows.Select(row => new OwnerRow(row.State, row.AppStatus, row.Name, row.Path)).ToList();
    }

    public async Task<IReadOnlyList<OrphanSource>> GetNotDetectedMappingsAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<OrphanSourceRow>(new CommandDefinition(
            $"""
            SELECT a.AppKey, a.Name, a.DetectionSource, a.InstallLocation, a.LastDetectedAt,
                   (SELECT COUNT(*) FROM ApplicationSnapshots p WHERE p.ApplicationId = a.Id AND p.Status IN ('{ApplicationStatus.Installed}', '{ApplicationStatus.Portable}')) AS InstalledSnapshots,
                   s.Path, s.SizeBytes,
                   COALESCE((SELECT group_concat(e.Description, ' ') FROM OwnershipEvidence e WHERE e.MappingId = m.Id), '') AS Evidence,
                   EXISTS(SELECT 1 FROM OwnershipEvidence e WHERE e.MappingId = m.Id AND e.EvidenceType IN ('ExactSignature', 'KnownPath', 'ExecutableMetadata', 'InstallPath')) AS HasStrongEvidence
            FROM ApplicationStorageMappings m
            JOIN Applications a ON a.Id = m.ApplicationId
            JOIN StorageLocations s ON s.Id = m.StorageLocationId
            WHERE a.Status = '{ApplicationStatus.NotDetected}' AND m.UserDecision IS NOT 'Rejected'
            """,
            cancellationToken: cancellationToken));
        return rows.Select(row => new OrphanSource(row.AppKey, row.Name, row.DetectionSource, row.InstallLocation, row.LastDetectedAt, (int)row.InstalledSnapshots, row.Path, row.SizeBytes, row.Evidence, row.HasStrongEvidence != 0)).ToList();
    }

    public async Task<IReadOnlyList<UnknownFolder>> GetUnknownFoldersAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<FolderRow>(new CommandDefinition(
            $"""
            SELECT s.Path, s.SizeBytes, i.LastModifiedAt
            FROM StorageLocations s
            JOIN ScanItems i ON i.Path = s.Path AND i.ScanSessionId = {LatestScan}
            WHERE s.OwnershipStatus = 'UnknownPreserved' AND s.DataType = 'ApplicationData'
              AND NOT EXISTS (SELECT 1 FROM ApplicationStorageMappings m WHERE m.StorageLocationId = s.Id)
            """,
            cancellationToken: cancellationToken));
        return rows.Select(row => new UnknownFolder(row.Path, row.SizeBytes, row.LastModifiedAt)).ToList();
    }

    public async Task SaveOrphansAsync(IReadOnlyList<OrphanCandidate> candidates, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync("CREATE TEMP TABLE Seen (Path TEXT PRIMARY KEY COLLATE NOCASE)", transaction: transaction);
        foreach (var candidate in candidates)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO OrphanCandidates (AppKey, ApplicationName, Path, Classification, SizeBytes, LastDetectedAt, Evidence)
                VALUES (@AppKey, @ApplicationName, @Path, @Classification, @SizeBytes, @LastDetectedAt, @Evidence)
                ON CONFLICT(Path) DO UPDATE SET AppKey = excluded.AppKey, ApplicationName = excluded.ApplicationName,
                    Classification = excluded.Classification, SizeBytes = excluded.SizeBytes,
                    LastDetectedAt = excluded.LastDetectedAt, Evidence = excluded.Evidence;
                INSERT OR IGNORE INTO Seen (Path) VALUES (@Path);
                """,
                candidate,
                transaction);
        }

        await connection.ExecuteAsync("DELETE FROM OrphanCandidates WHERE Path NOT IN (SELECT Path FROM Seen); DROP TABLE Seen;", transaction: transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrphanCandidate>> GetOrphansAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return (await connection.QueryAsync<OrphanCandidate>(new CommandDefinition(
            "SELECT Id, AppKey, ApplicationName, Path, Classification, Decision, SizeBytes, LastDetectedAt, Evidence FROM OrphanCandidates ORDER BY SizeBytes DESC",
            cancellationToken: cancellationToken))).ToList();
    }

    public async Task SetOrphanDecisionAsync(long id, string decision, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition("UPDATE OrphanCandidates SET Decision = @decision WHERE Id = @id", new { id, decision }, cancellationToken: cancellationToken));
    }

    public async Task<string?> GetOrphanDecisionAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT Decision FROM OrphanCandidates WHERE Path = @path", new { path }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<FolderRow>> GetScanItemsUnderAsync(string target, bool childrenOnly, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<FolderRow>(new CommandDefinition(
            childrenOnly
                ? $"""
                  SELECT Path, SizeBytes, ItemCount, LastModifiedAt, ItemType FROM ScanItems
                  WHERE ScanSessionId = {LatestScan} AND ItemType IN ('File', 'Folder')
                    AND lower(substr(Path, 1, length(@target) + 1)) = lower(@target || '\')
                    AND instr(substr(Path, length(@target) + 2), '\') = 0
                  """
                : $"SELECT Path, SizeBytes, ItemCount, LastModifiedAt, ItemType FROM ScanItems WHERE ScanSessionId = {LatestScan} AND ItemType = 'Folder' AND Path = @target",
            new { target },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<long> CreatePlanAsync(IReadOnlyList<PlanItem> items, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var valid = items.Where(item => item.IsValid).ToList();
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var planId = await connection.ExecuteScalarAsync<long>(
            "INSERT INTO CleanupPlans (CreatedAt, Status, SelectedItems, EstimatedBytes) VALUES (@at, @status, @count, @bytes) RETURNING Id",
            new { at = UtcTimestamp.Format(createdAt), status = PlanStatus.Planned, count = valid.Count, bytes = valid.Sum(item => item.SizeBytes) },
            transaction);
        await connection.ExecuteAsync(
            """
            INSERT INTO CleanupPlanItems (CleanupPlanId, ScanItemId, Selected, ValidationStatus, Path, SourceKind, AppKey, RuleId, SizeBytes, ItemCount, Fingerprint, BlockReason)
            VALUES (@PlanId, @ScanItemId, @Selected, @ValidationStatus, @Path, @SourceKind, @AppKey, @RuleId, @SizeBytes, @ItemCount, @Fingerprint, @BlockReason)
            """,
            items.Select(item => new { PlanId = planId, item.ScanItemId, Selected = item.IsValid, item.ValidationStatus, item.Path, item.SourceKind, item.AppKey, item.RuleId, item.SizeBytes, item.ItemCount, item.Fingerprint, item.BlockReason }),
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return planId;
    }

    public async Task<PlanSummary?> GetPlanAsync(long planId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.QuerySingleOrDefaultAsync<PlanSummary>(new CommandDefinition(
            "SELECT Id, CreatedAt, Status, SelectedItems, EstimatedBytes, ApprovedAt FROM CleanupPlans WHERE Id = @planId",
            new { planId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<PlanItem>> GetPlanItemsAsync(long planId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return (await connection.QueryAsync<PlanItem>(new CommandDefinition(
            "SELECT Id, CleanupPlanId, ScanItemId, Path, SourceKind, AppKey, RuleId, SizeBytes, ItemCount, Fingerprint, ValidationStatus, BlockReason FROM CleanupPlanItems WHERE CleanupPlanId = @planId",
            new { planId },
            cancellationToken: cancellationToken))).ToList();
    }

    public async Task<bool> ApprovePlanAsync(long planId, DateTimeOffset approvedAt, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE CleanupPlans SET Status = @approved, ApprovedAt = @at WHERE Id = @planId AND Status = @planned AND SelectedItems > 0",
            new { planId, approved = PlanStatus.Approved, planned = PlanStatus.Planned, at = UtcTimestamp.Format(approvedAt) },
            cancellationToken: cancellationToken)) == 1;
    }

    public async Task<bool> ClaimPlanAsync(long planId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE CleanupPlans SET Status = @progress WHERE Id = @planId AND Status = @approved",
            new { planId, progress = PlanStatus.InProgress, approved = PlanStatus.Approved },
            cancellationToken: cancellationToken)) == 1;
    }

    public async Task SetPlanStatusAsync(long planId, string status, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition("UPDATE CleanupPlans SET Status = @status WHERE Id = @planId", new { planId, status }, cancellationToken: cancellationToken));
    }

    public async Task<long> CreateOperationAsync(long planId, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO QuarantineOperations (CleanupPlanId, CreatedAt, Status) VALUES (@planId, @at, 'InProgress') RETURNING Id",
            new { planId, at = UtcTimestamp.Format(createdAt) },
            cancellationToken: cancellationToken));
    }

    public async Task SetOperationStatusAsync(long operationId, string status, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition("UPDATE QuarantineOperations SET Status = @status WHERE Id = @operationId", new { operationId, status }, cancellationToken: cancellationToken));
    }

    public async Task<long> AddQuarantineItemAsync(long operationId, PlanItem item, string quarantinePath, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO QuarantineItems (OperationId, OriginalPath, QuarantinePath, SizeBytes, ItemCount, Status, AppKey, CreatedAt)
            VALUES (@operationId, @Path, @quarantinePath, @SizeBytes, @ItemCount, @status, @AppKey, @createdAt)
            RETURNING Id
            """,
            new { operationId, item.Path, quarantinePath, item.SizeBytes, item.ItemCount, status = ItemState.InProgress, item.AppKey, createdAt = UtcTimestamp.Format(at) },
            cancellationToken: cancellationToken));
    }

    public async Task SetQuarantinePathAsync(long itemId, string quarantinePath, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition("UPDATE QuarantineItems SET QuarantinePath = @quarantinePath WHERE Id = @itemId", new { itemId, quarantinePath }, cancellationToken: cancellationToken));
    }

    public async Task SetItemStateAsync(long itemId, string status, string? message, string? hash, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE QuarantineItems SET Status = @status, Message = @message, VerificationHash = COALESCE(@hash, VerificationHash) WHERE Id = @itemId",
            new { itemId, status, message, hash },
            cancellationToken: cancellationToken));
    }

    public async Task SetRestoredPathAsync(long itemId, string path, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition("UPDATE QuarantineItems SET RestoredPath = @path WHERE Id = @itemId", new { itemId, path }, cancellationToken: cancellationToken));
    }

    public async Task<QuarantineItemView?> GetItemAsync(long itemId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.QuerySingleOrDefaultAsync<QuarantineItemView>(new CommandDefinition(
            $"SELECT {ItemColumns} FROM QuarantineItems WHERE Id = @itemId", new { itemId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<QuarantineItemView>> GetItemsAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return (await connection.QueryAsync<QuarantineItemView>(new CommandDefinition(
            $"SELECT {ItemColumns} FROM QuarantineItems ORDER BY Id DESC", cancellationToken: cancellationToken))).ToList();
    }

    public async Task<IReadOnlyList<QuarantineItemView>> GetIncompleteItemsAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return (await connection.QueryAsync<QuarantineItemView>(new CommandDefinition(
            $"SELECT {ItemColumns} FROM QuarantineItems WHERE Status IN ('{ItemState.InProgress}', '{ItemState.Restoring}', '{ItemState.Deleting}')",
            cancellationToken: cancellationToken))).ToList();
    }

    public async Task RecordRestoreAsync(long itemId, string status, string? path, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var timestamp = UtcTimestamp.Format(at);
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO RestoreOperations (StartedAt, CompletedAt, Status, QuarantineItemId, RestoredPath) VALUES (@timestamp, @timestamp, @status, @itemId, @path)",
            new { timestamp, status, itemId, path },
            cancellationToken: cancellationToken));
    }

    public async Task RecordDeletionAsync(long itemId, string status, long bytes, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO DeletionOperations (QuarantineItemId, DeletedAt, Status, Bytes) VALUES (@itemId, @at, @status, @bytes)",
            new { itemId, at = UtcTimestamp.Format(at), status, bytes },
            cancellationToken: cancellationToken));
    }

    private sealed class OrphanSourceRow
    {
        public string AppKey { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? DetectionSource { get; set; }

        public string? InstallLocation { get; set; }

        public string LastDetectedAt { get; set; } = string.Empty;

        public long InstalledSnapshots { get; set; }

        public string Path { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public string Evidence { get; set; } = string.Empty;

        public long HasStrongEvidence { get; set; }
    }
}
