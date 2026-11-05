using Microsoft.Data.Sqlite;

namespace WCM.Infrastructure.SQLite;

public sealed class SqliteConnectionFactory(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true,
        Pooling = false,
        DefaultTimeout = 30,
    }.ToString();

    public string DatabasePath { get; } = databasePath;

    public SqliteConnection Create() => new(_connectionString);
}
