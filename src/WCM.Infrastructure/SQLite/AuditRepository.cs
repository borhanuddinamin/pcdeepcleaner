using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;

namespace WCM.Infrastructure.SQLite;

public sealed class AuditRepository(SqliteConnectionFactory connections) : IAuditRepository
{
    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO AuditLogs (Timestamp, Action, TargetPath, Result, Details) VALUES (@Timestamp, @Action, @TargetPath, @Result, @Details)",
            new
            {
                Timestamp = UtcTimestamp.Format(entry.Timestamp),
                entry.Action,
                entry.TargetPath,
                entry.Result,
                entry.Details,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<AuditRow>(new CommandDefinition(
            "SELECT Timestamp, Action, TargetPath, Result, Details FROM AuditLogs ORDER BY Id DESC LIMIT @limit",
            new { limit },
            cancellationToken: cancellationToken));
        return rows
            .Select(row => new AuditEntry(UtcTimestamp.Parse(row.Timestamp), row.Action, row.TargetPath, row.Result, row.Details))
            .ToList();
    }
}
