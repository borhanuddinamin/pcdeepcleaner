using System.Text.Json;
using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Exceptions;
using WCM.Application.Interfaces;

namespace WCM.Infrastructure.SQLite;

public sealed class ScanConfigurationStore(SqliteConnectionFactory connections, ScanConfiguration defaults) : IScanConfigurationStore
{
    private const string Key = "ScanConfiguration";

    public async Task<ScanConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var json = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT Value FROM AppSettings WHERE Key = @Key",
            new { Key },
            cancellationToken: cancellationToken));

        if (json is null)
        {
            return defaults;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<ScanConfiguration>(json)
                ?? throw new DatabaseStateException("The saved scan settings are empty.");

            // Exclusions in the defaults, such as WCM's own data, always apply.
            var missing = defaults.Exclusions.Where(required =>
                !saved.Exclusions.Any(e => e.Path.Equals(required.Path, StringComparison.OrdinalIgnoreCase)));
            return saved with { Exclusions = [.. saved.Exclusions, .. missing] };
        }
        catch (JsonException ex)
        {
            throw new DatabaseStateException("The saved scan settings can't be read.", ex);
        }
    }

    public async Task SaveAsync(ScanConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO AppSettings (Key, Value, UpdatedAt) VALUES (@Key, @Value, @UpdatedAt)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, UpdatedAt = excluded.UpdatedAt
            """,
            new { Key, Value = JsonSerializer.Serialize(configuration), UpdatedAt = UtcTimestamp.Format(DateTimeOffset.UtcNow) },
            cancellationToken: cancellationToken));
    }
}
