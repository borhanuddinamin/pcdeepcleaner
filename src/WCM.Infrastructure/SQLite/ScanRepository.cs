using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;

namespace WCM.Infrastructure.SQLite;

public sealed class ScanRepository(SqliteConnectionFactory connections) : IScanRepository
{
    public async Task<long> StartSessionAsync(DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO ScanSessions (StartedAt, Status) VALUES (@startedAt, 'Running') RETURNING Id",
            new { startedAt = UtcTimestamp.Format(startedAt) },
            cancellationToken: cancellationToken));
    }

    public async Task AddItemsAsync(long sessionId, IEnumerable<ScanItemRecord> batch, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ScanItems (ScanSessionId, Path, ItemType, SizeBytes, LastModifiedAt, IsLocked, IsProtected)
            VALUES (@SessionId, @Path, @ItemType, @SizeBytes, @LastModifiedAt, @IsLocked, @IsProtected)
            """,
            batch.Select(item => new
            {
                SessionId = sessionId,
                item.Path,
                item.ItemType,
                item.SizeBytes,
                LastModifiedAt = UtcTimestamp.Format(item.LastModifiedAt),
                item.IsLocked,
                item.IsProtected,
            }),
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompleteSessionAsync(
        long sessionId,
        DateTimeOffset completedAt,
        string status,
        long filesScanned,
        long foldersScanned,
        long errors,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ScanSessions
            SET CompletedAt = @completedAt, Status = @status, FilesScanned = @filesScanned,
                FoldersScanned = @foldersScanned, Errors = @errors
            WHERE Id = @sessionId
            """,
            new { sessionId, completedAt = UtcTimestamp.Format(completedAt), status, filesScanned, foldersScanned, errors },
            cancellationToken: cancellationToken));
    }

    public async Task<ScanSessionRecord?> GetSessionAsync(long sessionId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var row = await connection.QuerySingleOrDefaultAsync<ScanSessionRow>(new CommandDefinition(
            "SELECT Id, StartedAt, CompletedAt, Status, FilesScanned, FoldersScanned, Errors FROM ScanSessions WHERE Id = @sessionId",
            new { sessionId },
            cancellationToken: cancellationToken));

        return row is null
            ? null
            : new ScanSessionRecord(
                row.Id,
                UtcTimestamp.Parse(row.StartedAt),
                UtcTimestamp.ParseNullable(row.CompletedAt),
                row.Status,
                row.FilesScanned,
                row.FoldersScanned,
                row.Errors);
    }
}
