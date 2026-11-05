using Dapper;
using WCM.Application.Interfaces;

namespace WCM.Infrastructure.SQLite;

public sealed class SettingsStore(SqliteConnectionFactory connections) : ISettingsStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT Value FROM AppSettings WHERE Key = @key", new { key }, cancellationToken: cancellationToken));
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO AppSettings (Key, Value, UpdatedAt) VALUES (@key, @value, @at)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, UpdatedAt = excluded.UpdatedAt
            """,
            new { key, value, at = UtcTimestamp.Format(DateTimeOffset.UtcNow) },
            cancellationToken: cancellationToken));
    }
}
