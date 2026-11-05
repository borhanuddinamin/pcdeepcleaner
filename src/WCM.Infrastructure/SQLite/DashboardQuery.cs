using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;

namespace WCM.Infrastructure.SQLite;

public sealed class DashboardQuery(SqliteConnectionFactory connections) : IDashboardQuery
{
    private const string LatestScan = "(SELECT MAX(Id) FROM ScanSessions WHERE Status IN ('Completed', 'CompletedWithErrors'))";

    public async Task<DashboardStats> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.QuerySingleAsync<DashboardStats>(new CommandDefinition(
            $"""
            SELECT
              (SELECT COUNT(*) FROM Applications WHERE Status <> 'NotDetected') AS InstalledApplications,
              (SELECT COUNT(*) FROM OrphanCandidates WHERE Decision <> 'Kept') AS OrphanCandidates,
              0 AS CacheCandidates,
              COALESCE((SELECT TotalBytes FROM ScanSessions WHERE Id = {LatestScan}), 0) AS DiscoveredBytes,
              COALESCE((SELECT SUM(SizeBytes) FROM StorageLocations WHERE OwnershipStatus IN ('InstalledProtected', 'SharedProtected')), 0) AS ProtectedBytes,
              COALESCE((SELECT SUM(SizeBytes) FROM OrphanCandidates WHERE Decision <> 'Kept'), 0) AS CandidateBytes,
              COALESCE((SELECT SUM(EstimatedBytes) FROM CleanupPlans WHERE Status = 'Approved'), 0) AS EligibleBytes,
              COALESCE((SELECT SUM(SizeBytes) FROM QuarantineItems WHERE Status = 'Quarantined'), 0) AS QuarantinedBytes,
              COALESCE((SELECT SUM(Bytes) FROM DeletionOperations WHERE Status = 'PermanentlyDeleted'), 0) AS FreedBytes,
              (SELECT CompletedAt FROM ScanSessions WHERE Id = (SELECT MAX(Id) FROM ScanSessions)) AS LastScanAt,
              (SELECT Status FROM ScanSessions WHERE Id = (SELECT MAX(Id) FROM ScanSessions)) AS LastScanStatus,
              COALESCE((SELECT Errors FROM ScanSessions WHERE Id = (SELECT MAX(Id) FROM ScanSessions)), 0) AS LastScanErrors
            """,
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ScanHistoryRow>> GetScanHistoryAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return (await connection.QueryAsync<ScanHistoryRow>(new CommandDefinition(
            "SELECT Id, StartedAt, CompletedAt, Status, FilesScanned, FoldersScanned, Errors, TotalBytes, LinksSkipped FROM ScanSessions ORDER BY Id DESC LIMIT 200",
            cancellationToken: cancellationToken))).ToList();
    }

    public async Task<IReadOnlyList<FolderRow>> GetFoldersAsync(string? parent, string? search, CancellationToken cancellationToken)
    {
        var filter = !string.IsNullOrWhiteSpace(search)
            ? "AND instr(lower(i.Path), lower(@search)) > 0"
            : parent is null
                ? "AND s.Id IS NOT NULL"
                : @"AND lower(substr(i.Path, 1, length(@parent) + 1)) = lower(@parent || '\') AND instr(substr(i.Path, length(@parent) + 2), '\') = 0";
        await using var connection = connections.Create();
        return (await connection.QueryAsync<FolderRow>(new CommandDefinition(
            $"""
            SELECT i.Path, i.SizeBytes, i.ItemCount, i.LastModifiedAt, i.ItemType, i.IsPartial, s.DataType, s.OwnershipStatus,
                   (SELECT group_concat(a.Name, ', ') FROM ApplicationStorageMappings m JOIN Applications a ON a.Id = m.ApplicationId WHERE m.StorageLocationId = s.Id) AS Owners,
                   (SELECT MAX(m.ConfidenceScore) FROM ApplicationStorageMappings m WHERE m.StorageLocationId = s.Id) AS Confidence
            FROM ScanItems i
            LEFT JOIN StorageLocations s ON s.Path = i.Path
            WHERE i.ScanSessionId = {LatestScan} {filter}
            ORDER BY i.SizeBytes DESC
            LIMIT 500
            """,
            new { parent, search },
            cancellationToken: cancellationToken))).ToList();
    }

    public async Task<IReadOnlyList<ScanErrorRow>> GetScanErrorsAsync(long sessionId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return (await connection.QueryAsync<ScanErrorRow>(new CommandDefinition(
            "SELECT Path, Kind, Message FROM ScanErrors WHERE ScanSessionId = @sessionId LIMIT 1000",
            new { sessionId },
            cancellationToken: cancellationToken))).ToList();
    }
}
