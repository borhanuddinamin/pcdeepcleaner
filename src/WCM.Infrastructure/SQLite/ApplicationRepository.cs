using System.Data.Common;
using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Enums;

namespace WCM.Infrastructure.SQLite;

public sealed class ApplicationRepository(SqliteConnectionFactory connections) : IApplicationRepository
{
    private const string Columns =
        "AppKey, Name, Publisher, Version, InstallLocation, ExecutablePath, DetectionSource, Status, FirstDetectedAt, LastDetectedAt, ReviewReason";

    private const string UpsertSql =
        $"""
        INSERT INTO Applications ({Columns})
        VALUES (@AppKey, @Name, @Publisher, @Version, @InstallLocation, @ExecutablePath, @DetectionSource, @Status, @FirstDetectedAt, @LastDetectedAt, @ReviewReason)
        ON CONFLICT(AppKey) DO UPDATE SET
            Name = excluded.Name,
            Publisher = excluded.Publisher,
            Version = excluded.Version,
            InstallLocation = excluded.InstallLocation,
            ExecutablePath = excluded.ExecutablePath,
            DetectionSource = excluded.DetectionSource,
            Status = excluded.Status,
            LastDetectedAt = excluded.LastDetectedAt,
            ReviewReason = excluded.ReviewReason
        RETURNING Id
        """;

    public async Task UpsertAsync(ApplicationRecord application, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            UpsertSql,
            new
            {
                application.AppKey,
                application.Name,
                application.Publisher,
                application.Version,
                application.InstallLocation,
                application.ExecutablePath,
                application.DetectionSource,
                application.Status,
                FirstDetectedAt = UtcTimestamp.Format(application.FirstDetectedAt),
                LastDetectedAt = UtcTimestamp.Format(application.LastDetectedAt),
                application.ReviewReason,
            },
            cancellationToken: cancellationToken));
    }

    public async Task SaveInventoryAsync(
        IReadOnlyList<MergedApplication> applications,
        DateTimeOffset observedAt,
        bool isComplete,
        CancellationToken cancellationToken)
    {
        var timestamp = UtcTimestamp.Format(observedAt);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var merged in applications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var application = merged.Application;
            seen.Add(application.AppKey);
            var id = await connection.ExecuteScalarAsync<long>(
                UpsertSql,
                new
                {
                    application.AppKey,
                    application.Name,
                    application.Publisher,
                    application.Version,
                    application.InstallLocation,
                    application.ExecutablePath,
                    application.DetectionSource,
                    application.Status,
                    FirstDetectedAt = timestamp,
                    LastDetectedAt = timestamp,
                    merged.ReviewReason,
                },
                transaction);

            await connection.ExecuteAsync(
                "DELETE FROM ApplicationSourceRecords WHERE ApplicationId = @id",
                new { id },
                transaction);
            await connection.ExecuteAsync(
                """
                INSERT INTO ApplicationSourceRecords
                    (ApplicationId, SourceKey, DetectionSource, Name, Publisher, Version, InstallLocation, ExecutablePath, Architecture, Status, ObservedAt)
                VALUES
                    (@Id, @AppKey, @DetectionSource, @Name, @Publisher, @Version, @InstallLocation, @ExecutablePath, @Architecture, @Status, @Timestamp)
                """,
                merged.Sources.Select(source => new
                {
                    Id = id,
                    source.AppKey,
                    source.DetectionSource,
                    source.Name,
                    source.Publisher,
                    source.Version,
                    source.InstallLocation,
                    source.ExecutablePath,
                    source.Architecture,
                    source.Status,
                    Timestamp = timestamp,
                }),
                transaction);
            await AddSnapshotAsync(connection, transaction, id, timestamp, application.Status, application.InstallLocation);
        }

        if (isComplete)
        {
            var absent = (await connection.QueryAsync<AbsentApplicationRow>(
                    "SELECT Id, AppKey, InstallLocation FROM Applications WHERE Status <> @status",
                    new { status = ApplicationStatus.NotDetected },
                    transaction))
                .Where(row => !seen.Contains(row.AppKey));

            foreach (var row in absent)
            {
                await connection.ExecuteAsync(
                    "UPDATE Applications SET Status = @status WHERE Id = @Id",
                    new { status = ApplicationStatus.NotDetected, row.Id },
                    transaction);
                await AddSnapshotAsync(connection, transaction, row.Id, timestamp, ApplicationStatus.NotDetected, row.InstallLocation);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ApplicationRecord?> GetByKeyAsync(string appKey, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var row = await connection.QuerySingleOrDefaultAsync<ApplicationRow>(new CommandDefinition(
            $"SELECT {Columns} FROM Applications WHERE AppKey = @appKey",
            new { appKey },
            cancellationToken: cancellationToken));
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<ApplicationRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<ApplicationRow>(new CommandDefinition(
            $"SELECT {Columns} FROM Applications ORDER BY Name COLLATE NOCASE",
            cancellationToken: cancellationToken));
        return rows.Select(ToRecord).ToList();
    }

    private static Task AddSnapshotAsync(
        DbConnection connection,
        DbTransaction transaction,
        long applicationId,
        string timestamp,
        string status,
        string? installLocation) =>
        connection.ExecuteAsync(
            "INSERT INTO ApplicationSnapshots (ApplicationId, SnapshotAt, Status, InstallLocation) VALUES (@applicationId, @timestamp, @status, @installLocation)",
            new { applicationId, timestamp, status, installLocation },
            transaction);

    private static ApplicationRecord ToRecord(ApplicationRow row) => new(
        row.AppKey,
        row.Name,
        row.Publisher,
        row.Version,
        row.InstallLocation,
        row.ExecutablePath,
        row.DetectionSource,
        row.Status,
        UtcTimestamp.Parse(row.FirstDetectedAt),
        UtcTimestamp.Parse(row.LastDetectedAt),
        row.ReviewReason);

    private sealed class AbsentApplicationRow
    {
        public long Id { get; set; }

        public string AppKey { get; set; } = string.Empty;

        public string? InstallLocation { get; set; }
    }
}
