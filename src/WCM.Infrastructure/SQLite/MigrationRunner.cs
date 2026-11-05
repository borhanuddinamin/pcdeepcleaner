using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using WCM.Application.Exceptions;

namespace WCM.Infrastructure.SQLite;

public sealed record Migration(int Version, string Name, string Sql)
{
    // Line endings are normalized so a Git checkout can't change the checksum.
    public string Checksum => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Sql.ReplaceLineEndings("\n"))));
}

public static class MigrationRunner
{
    public static IReadOnlyList<Migration> LoadEmbedded()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(name => Load(assembly, name))
            .OrderBy(migration => migration.Version)
            .ToList();
    }

    public static void Apply(SqliteConnection connection, IReadOnlyList<Migration> migrations)
    {
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Version INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Checksum TEXT NOT NULL,
                AppliedAt TEXT NOT NULL
            );
            """);

        var applied = connection.Query<AppliedMigrationRow>(
            "SELECT Version, Checksum FROM SchemaMigrations").ToList();

        foreach (var row in applied)
        {
            var known = migrations.FirstOrDefault(migration => migration.Version == row.Version)
                ?? throw new DatabaseStateException(
                    $"The database uses schema version {row.Version}, which this version of WCM doesn't know.");

            if (!known.Checksum.Equals(row.Checksum, StringComparison.Ordinal))
            {
                throw new DatabaseStateException($"Schema version {row.Version} doesn't match its recorded checksum.");
            }
        }

        foreach (var migration in migrations.Where(m => applied.All(row => row.Version != m.Version)))
        {
            using var transaction = connection.BeginTransaction();
            connection.Execute(migration.Sql, transaction: transaction);
            connection.Execute(
                "INSERT INTO SchemaMigrations (Version, Name, Checksum, AppliedAt) VALUES (@Version, @Name, @Checksum, @AppliedAt)",
                new
                {
                    migration.Version,
                    migration.Name,
                    migration.Checksum,
                    AppliedAt = UtcTimestamp.Format(DateTimeOffset.UtcNow),
                },
                transaction);
            transaction.Commit();
        }
    }

    private static Migration Load(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var name = Path.GetFileNameWithoutExtension(resourceName);
        return new Migration(int.Parse(name.Split('_')[0], System.Globalization.CultureInfo.InvariantCulture), name, reader.ReadToEnd());
    }
}
