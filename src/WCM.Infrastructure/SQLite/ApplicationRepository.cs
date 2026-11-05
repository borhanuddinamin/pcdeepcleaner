using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;

namespace WCM.Infrastructure.SQLite;

public sealed class ApplicationRepository(SqliteConnectionFactory connections) : IApplicationRepository
{
    private const string Columns =
        "AppKey, Name, Publisher, Version, InstallLocation, ExecutablePath, DetectionSource, Status, FirstDetectedAt, LastDetectedAt";

    public async Task UpsertAsync(ApplicationRecord application, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            $"""
            INSERT INTO Applications ({Columns})
            VALUES (@AppKey, @Name, @Publisher, @Version, @InstallLocation, @ExecutablePath, @DetectionSource, @Status, @FirstDetectedAt, @LastDetectedAt)
            ON CONFLICT(AppKey) DO UPDATE SET
                Name = excluded.Name,
                Publisher = excluded.Publisher,
                Version = excluded.Version,
                InstallLocation = excluded.InstallLocation,
                ExecutablePath = excluded.ExecutablePath,
                DetectionSource = excluded.DetectionSource,
                Status = excluded.Status,
                LastDetectedAt = excluded.LastDetectedAt
            """,
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
            },
            cancellationToken: cancellationToken));
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
        UtcTimestamp.Parse(row.LastDetectedAt));
}
