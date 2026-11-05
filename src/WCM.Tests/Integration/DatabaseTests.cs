using Dapper;
using Microsoft.Data.Sqlite;
using WCM.Application.DTOs;
using WCM.Application.Exceptions;
using WCM.Infrastructure.SQLite;
using WCM.Infrastructure.Windows;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Integration;

public sealed class DatabaseTests : IDisposable
{
    private static readonly DateTimeOffset Time = new(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(6));

    private readonly SyntheticDataFixture _fixture = new();
    private readonly SqliteConnectionFactory _connections;
    private readonly DatabaseInitializer _initializer;

    public DatabaseTests()
    {
        _connections = new SqliteConnectionFactory(Path.Combine(_fixture.Root, "Data", "cleanup.db"));
        _initializer = new DatabaseInitializer(_connections);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Initialize_creates_all_specified_tables_and_is_repeatable()
    {
        _initializer.Initialize();
        _initializer.Initialize();

        using var connection = _connections.Create();
        var tables = connection.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table'").ToHashSet();
        Assert.Superset(
            new HashSet<string>
            {
                "Applications", "ApplicationSnapshots", "StorageLocations", "ApplicationStorageMappings",
                "OwnershipEvidence", "ScanSessions", "ScanItems", "CleanupRules", "ProtectedPaths", "CleanupPlans",
                "CleanupPlanItems", "QuarantineOperations", "QuarantineItems", "RestoreOperations",
                "DeletionOperations", "AuditLogs", "AppSettings",
            },
            tables);
        Assert.Equal(4, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SchemaMigrations"));
        Assert.Equal("wal", connection.ExecuteScalar<string>("PRAGMA journal_mode;"));
    }

    [Fact]
    public void Foreign_keys_are_enforced()
    {
        _initializer.Initialize();
        using var connection = _connections.Create();

        Assert.Throws<SqliteException>(() => connection.Execute(
            "INSERT INTO ApplicationSnapshots (ApplicationId, SnapshotAt, Status) VALUES (999, 'x', 'x')"));
    }

    [Fact]
    public void Confidence_outside_range_is_rejected()
    {
        _initializer.Initialize();
        using var connection = _connections.Create();
        connection.Execute(
            """
            INSERT INTO Applications (AppKey, Name, DetectionSource, Status, FirstDetectedAt, LastDetectedAt) VALUES ('a', 'a', 's', 's', 't', 't');
            INSERT INTO StorageLocations (Path, DataType) VALUES ('C:\a', 'Unknown');
            """);

        Assert.Throws<SqliteException>(() => connection.Execute(
            "INSERT INTO ApplicationStorageMappings (ApplicationId, StorageLocationId, ConfidenceScore, OwnershipStatus) VALUES (1, 1, 101, 'x')"));
    }

    [Fact]
    public void Storage_paths_are_unique_regardless_of_case()
    {
        _initializer.Initialize();
        using var connection = _connections.Create();
        connection.Execute(@"INSERT INTO StorageLocations (Path, DataType) VALUES ('C:\App', 'Unknown')");

        Assert.Throws<SqliteException>(() => connection.Execute(
            @"INSERT INTO StorageLocations (Path, DataType) VALUES ('c:\APP', 'Unknown')"));
    }

    [Fact]
    public void New_rules_default_to_cleanup_disabled_and_mappings_to_protected()
    {
        _initializer.Initialize();
        using var connection = _connections.Create();
        connection.Execute(
            "INSERT INTO CleanupRules (AppKey, RelativePathPattern, DataType, RuleVersion) VALUES ('a', 'Cache', 'Cache', '1')");

        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT CleanupAllowed FROM CleanupRules"));
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT RequiresAppClosed FROM CleanupRules"));
    }

    [Fact]
    public void Changed_migration_checksum_fails_closed()
    {
        _initializer.Initialize();
        using (var connection = _connections.Create())
        {
            connection.Execute("UPDATE SchemaMigrations SET Checksum = 'changed'");
        }

        Assert.Throws<DatabaseStateException>(_initializer.Initialize);
    }

    [Fact]
    public void Newer_database_schema_fails_closed()
    {
        _initializer.Initialize();
        using (var connection = _connections.Create())
        {
            connection.Execute("INSERT INTO SchemaMigrations VALUES (999, 'future', 'x', 't')");
        }

        Assert.Throws<DatabaseStateException>(_initializer.Initialize);
    }

    [Fact]
    public void Failed_migration_rolls_back_completely()
    {
        var migrations = new[] { new Migration(1, "bad", "CREATE TABLE Partial (Id INTEGER); THIS IS NOT SQL;") };

        Assert.Throws<DatabaseStateException>(new DatabaseInitializer(_connections, migrations).Initialize);

        using var connection = _connections.Create();
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Partial'"));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SchemaMigrations"));
    }

    [Fact]
    public void Corrupt_database_fails_closed_and_is_left_untouched()
    {
        var path = _fixture.AddFile(@"Data\cleanup.db", new string('x', 8192));
        var before = File.ReadAllBytes(path);

        Assert.Throws<DatabaseStateException>(_initializer.Initialize);

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Backup_can_replace_a_lost_database_and_never_overwrites()
    {
        _initializer.Initialize();
        await new AuditRepository(_connections).AppendAsync(
            new AuditEntry(Time, "Test", null, "Ok", null), CancellationToken.None);
        var backup = Path.Combine(_fixture.Root, "backup.db");

        _initializer.BackupTo(backup);

        var restored = new SqliteConnectionFactory(backup);
        new DatabaseInitializer(restored).Initialize();
        Assert.Single(await new AuditRepository(restored).GetRecentAsync(10, CancellationToken.None));
        Assert.Throws<IOException>(() => _initializer.BackupTo(backup));
    }

    [Fact]
    public async Task Upsert_keeps_first_detection_and_stores_utc()
    {
        _initializer.Initialize();
        var repository = new ApplicationRepository(_connections);
        var first = new ApplicationRecord("key", "App", "Pub", "1", null, null, "Registry", "Installed", Time, Time);

        await repository.UpsertAsync(first, CancellationToken.None);
        await repository.UpsertAsync(
            first with { Version = "2", FirstDetectedAt = Time.AddDays(5), LastDetectedAt = Time.AddDays(5) },
            CancellationToken.None);

        var stored = await repository.GetByKeyAsync("key", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("2", stored.Version);
        Assert.Equal(Time, stored.FirstDetectedAt);
        Assert.Equal(Time.AddDays(5), stored.LastDetectedAt);
        Assert.Single(await repository.GetAllAsync(CancellationToken.None));

        using var connection = _connections.Create();
        Assert.Equal(
            "2026-01-01T21:04:05.0000000Z",
            connection.ExecuteScalar<string>("SELECT FirstDetectedAt FROM Applications"));
    }

    [Fact]
    public async Task Scan_session_and_large_sizes_round_trip()
    {
        _initializer.Initialize();
        var repository = new ScanRepository(_connections);

        var id = await repository.StartSessionAsync(Time, CancellationToken.None);
        await repository.AddItemsAsync(
            id,
            [new ScanItemRecord(@"C:\a", "File", 5_000_000_000_000, Time, false, true), new ScanItemRecord(@"C:\b", "Folder", 0, null, true, false)],
            CancellationToken.None);
        await repository.CompleteSessionAsync(id, Time.AddMinutes(1), "Completed", 1, 1, 0, 100, 2, CancellationToken.None);

        var session = await repository.GetSessionAsync(id, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal("Completed", session.Status);
        Assert.Equal(Time.AddMinutes(1), session.CompletedAt);

        using var connection = _connections.Create();
        Assert.Equal(5_000_000_000_000, connection.ExecuteScalar<long>("SELECT MAX(SizeBytes) FROM ScanItems"));
    }

    [Fact]
    public async Task Audit_entries_return_newest_first()
    {
        _initializer.Initialize();
        var repository = new AuditRepository(_connections);

        await repository.AppendAsync(new AuditEntry(Time, "First", null, "Ok", null), CancellationToken.None);
        await repository.AppendAsync(new AuditEntry(Time, "Second", @"C:\a", "Ok", "d"), CancellationToken.None);

        var entries = await repository.GetRecentAsync(1, CancellationToken.None);
        Assert.Equal("Second", Assert.Single(entries).Action);
    }

    [Fact]
    public void Database_path_inside_a_protected_location_is_refused()
    {
        var exe = Path.Combine(_fixture.Root, "Program Files", "WCM");
        _fixture.AddFile(@"Program Files\WCM\portable.marker");
        var service = new DataDirectoryService(
            Path.Combine(_fixture.Root, "settings"), exe, [Path.Combine(_fixture.Root, "Program Files")]);

        Assert.Throws<InvalidDataException>(() => service.DatabasePath);
    }
}
