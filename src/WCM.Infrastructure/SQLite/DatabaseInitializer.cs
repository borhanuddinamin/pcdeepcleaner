using Dapper;
using Microsoft.Data.Sqlite;
using WCM.Application.Exceptions;
using WCM.Application.Interfaces;

namespace WCM.Infrastructure.SQLite;

public sealed class DatabaseInitializer(SqliteConnectionFactory connections) : IDatabaseInitializer
{
    private readonly IReadOnlyList<Migration> _migrations = MigrationRunner.LoadEmbedded();

    internal DatabaseInitializer(SqliteConnectionFactory connections, IReadOnlyList<Migration> migrations)
        : this(connections)
    {
        _migrations = migrations;
    }

    public void Initialize()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(connections.DatabasePath)!);
            using var connection = connections.Create();
            connection.Open();
            QuickCheck(connection);
            connection.Execute("PRAGMA journal_mode = WAL;");
            if (MigrationRunner.Apply(connection, _migrations))
            {
                FullCheck(connection);
            }
        }
        catch (SqliteException ex)
        {
            // The file is left in place so it can be recovered or inspected.
            throw new DatabaseStateException($"The database can't be used: {connections.DatabasePath}", ex);
        }
    }

    public void BackupTo(string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            throw new IOException($"The backup file already exists: {destinationPath}");
        }

        using var source = connections.Create();
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Pooling = false,
        }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    // Fast enough for every start. It finds file corruption, but not every index mismatch.
    private void QuickCheck(SqliteConnection connection)
    {
        if (connection.Query<string>("PRAGMA quick_check(1);").ToList() is not ["ok"])
        {
            throw new DatabaseStateException($"The database failed its integrity check: {connections.DatabasePath}");
        }
    }

    private void FullCheck(SqliteConnection connection)
    {
        var integrity = connection.Query<string>("PRAGMA integrity_check;").ToList();
        if (integrity is not ["ok"])
        {
            throw new DatabaseStateException($"The database failed its integrity check: {connections.DatabasePath}");
        }

        if (connection.Query("PRAGMA foreign_key_check;").Any())
        {
            throw new DatabaseStateException($"The database has broken references: {connections.DatabasePath}");
        }
    }
}
