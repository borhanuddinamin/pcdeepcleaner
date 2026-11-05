using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Policies;

namespace WCM.Infrastructure.SQLite;

public sealed class MappingRepository(SqliteConnectionFactory connections) : IMappingRepository
{
    public async Task<IReadOnlyList<PriorMapping>> GetPriorMappingsAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<PriorRow>(new CommandDefinition(
            """
            SELECT a.AppKey, s.Path, m.UserDecision
            FROM ApplicationStorageMappings m
            JOIN Applications a ON a.Id = m.ApplicationId
            JOIN StorageLocations s ON s.Id = m.StorageLocationId
            """,
            cancellationToken: cancellationToken));
        return rows.Select(row => new PriorMapping(row.AppKey, row.Path, row.UserDecision)).ToList();
    }

    public async Task SavePlanAsync(MappingPlan plan, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var timestamp = UtcTimestamp.Format(observedAt);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var locationIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in plan.Locations)
        {
            locationIds[location.Location.Path] = await connection.ExecuteScalarAsync<long>(
                """
                INSERT INTO StorageLocations (Path, DataType, SizeBytes, FileCount, OwnershipStatus)
                VALUES (@Path, @DataType, @SizeBytes, @FileCount, @OwnershipStatus)
                ON CONFLICT(Path) DO UPDATE SET
                    DataType = excluded.DataType,
                    SizeBytes = excluded.SizeBytes,
                    FileCount = excluded.FileCount,
                    OwnershipStatus = excluded.OwnershipStatus
                RETURNING Id
                """,
                new
                {
                    location.Location.Path,
                    location.DataType,
                    location.Location.SizeBytes,
                    FileCount = location.Location.ItemCount,
                    location.OwnershipStatus,
                },
                transaction);
        }

        var applicationIds = (await connection.QueryAsync<(long Id, string AppKey)>(
                "SELECT Id, AppKey FROM Applications", transaction: transaction))
            .ToDictionary(row => row.AppKey, row => row.Id, StringComparer.Ordinal);

        foreach (var mapping in plan.Mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // IsProtected is always written as 1. Only the validated cleanup flow may look past it.
            var mappingId = await connection.ExecuteScalarAsync<long>(
                """
                INSERT INTO ApplicationStorageMappings (ApplicationId, StorageLocationId, ConfidenceScore, OwnershipStatus, IsProtected, LastMappedAt)
                VALUES (@ApplicationId, @LocationId, @Score, @State, 1, @Timestamp)
                ON CONFLICT(ApplicationId, StorageLocationId) DO UPDATE SET
                    ConfidenceScore = excluded.ConfidenceScore,
                    OwnershipStatus = excluded.OwnershipStatus,
                    IsProtected = 1,
                    LastMappedAt = excluded.LastMappedAt
                RETURNING Id
                """,
                new
                {
                    ApplicationId = applicationIds[mapping.AppKey],
                    LocationId = locationIds[mapping.Path],
                    mapping.Score,
                    mapping.State,
                    Timestamp = timestamp,
                },
                transaction);

            await connection.ExecuteAsync("DELETE FROM OwnershipEvidence WHERE MappingId = @mappingId", new { mappingId }, transaction);
            await connection.ExecuteAsync(
                "INSERT INTO OwnershipEvidence (MappingId, EvidenceType, Description, Score, ObservedAt) VALUES (@MappingId, @Type, @Description, @Score, @Timestamp)",
                mapping.Evidence.Select(item => new { MappingId = mappingId, item.Type, item.Description, item.Score, Timestamp = timestamp }),
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LocationMapping>> GetLocationsAsync(string appKey, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var rows = (await connection.QueryAsync<LocationRow>(new CommandDefinition(
            """
            SELECT m.Id AS MappingId, s.Path, s.DataType, s.SizeBytes, s.FileCount, m.ConfidenceScore, m.OwnershipStatus, m.IsProtected
            FROM ApplicationStorageMappings m
            JOIN Applications a ON a.Id = m.ApplicationId
            JOIN StorageLocations s ON s.Id = m.StorageLocationId
            WHERE a.AppKey = @appKey
            ORDER BY m.ConfidenceScore DESC, s.Path COLLATE NOCASE
            """,
            new { appKey },
            cancellationToken: cancellationToken))).ToList();

        var evidence = (await connection.QueryAsync<EvidenceRow>(new CommandDefinition(
            """
            SELECT e.MappingId, e.EvidenceType, e.Description, e.Score
            FROM OwnershipEvidence e
            JOIN ApplicationStorageMappings m ON m.Id = e.MappingId
            JOIN Applications a ON a.Id = m.ApplicationId
            WHERE a.AppKey = @appKey
            ORDER BY e.Id
            """,
            new { appKey },
            cancellationToken: cancellationToken)))
            .ToLookup(row => row.MappingId);

        return rows.Select(row => new LocationMapping(
                row.MappingId,
                row.Path,
                row.DataType,
                row.SizeBytes,
                row.FileCount,
                (int)row.ConfidenceScore,
                row.OwnershipStatus,
                row.IsProtected != 0,
                evidence[row.MappingId].Select(item => new EvidenceItem(item.EvidenceType, item.Description, (int)item.Score)).ToList()))
            .ToList();
    }

    public async Task SetDecisionAsync(long mappingId, string decision, DateTimeOffset decidedAt, CancellationToken cancellationToken)
    {
        var state = decision == UserDecision.Confirmed ? OwnershipState.UserConfirmedOwnership : OwnershipState.UserRejectedMapping;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(
            "UPDATE ApplicationStorageMappings SET UserDecision = @decision, DecidedAt = @at, OwnershipStatus = @state, IsProtected = 1 WHERE Id = @mappingId",
            new { decision, at = UtcTimestamp.Format(decidedAt), state, mappingId },
            transaction);
        await connection.ExecuteAsync(
            "INSERT INTO OwnershipEvidence (MappingId, EvidenceType, Description, Score, ObservedAt) VALUES (@mappingId, @Type, @Description, 0, @at)",
            new { mappingId, Type = EvidenceType.UserDecision, Description = $"You marked this link as {decision.ToLowerInvariant()}.", at = UtcTimestamp.Format(decidedAt) },
            transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed class PriorRow
    {
        public string AppKey { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string? UserDecision { get; set; }
    }

    private sealed class LocationRow
    {
        public long MappingId { get; set; }

        public string Path { get; set; } = string.Empty;

        public string DataType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public long FileCount { get; set; }

        public long ConfidenceScore { get; set; }

        public string OwnershipStatus { get; set; } = string.Empty;

        public long IsProtected { get; set; }
    }

    private sealed class EvidenceRow
    {
        public long MappingId { get; set; }

        public string EvidenceType { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public long Score { get; set; }
    }
}
