using WCM.Application.DTOs;

namespace WCM.Application.Interfaces;

public interface IDatabaseInitializer
{
    /// <summary>Checks integrity and applies pending migrations.</summary>
    /// <exception cref="Exceptions.DatabaseStateException">The database can't be trusted.</exception>
    void Initialize();

    /// <summary>Copies the database to a new file. Never overwrites an existing file.</summary>
    void BackupTo(string destinationPath);
}

public interface IApplicationRepository
{
    Task UpsertAsync(ApplicationRecord application, CancellationToken cancellationToken);

    Task<ApplicationRecord?> GetByKeyAsync(string appKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApplicationRecord>> GetAllAsync(CancellationToken cancellationToken);
}

public interface IScanRepository
{
    Task<long> StartSessionAsync(DateTimeOffset startedAt, CancellationToken cancellationToken);

    Task AddItemsAsync(long sessionId, IEnumerable<ScanItemRecord> batch, CancellationToken cancellationToken);

    Task CompleteSessionAsync(
        long sessionId,
        DateTimeOffset completedAt,
        string status,
        long filesScanned,
        long foldersScanned,
        long errors,
        CancellationToken cancellationToken);

    Task<ScanSessionRecord?> GetSessionAsync(long sessionId, CancellationToken cancellationToken);
}

/// <summary>Append-only: audit history has no update or delete operations.</summary>
public interface IAuditRepository
{
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken);
}
