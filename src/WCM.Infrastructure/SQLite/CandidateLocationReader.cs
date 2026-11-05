using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Infrastructure.FileSystem;

namespace WCM.Infrastructure.SQLite;

public sealed class CandidateLocationReader(SqliteConnectionFactory connections) : ICandidateLocationReader
{
    private const int MaxDepth = 2;

    public async Task<IReadOnlyList<CandidateLocation>?> ReadAsync(
        ScanConfiguration configuration,
        IReadOnlySet<string> alwaysInclude,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var sessionId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT MAX(Id) FROM ScanSessions WHERE Status IN ('Completed', 'CompletedWithErrors')",
            cancellationToken: cancellationToken));
        if (sessionId is null)
        {
            return null;
        }

        // Longest first, so a nested root such as Temp wins over the folder containing it.
        var roots = configuration.Roots
            .Select(root => (Path: PathNormalizer.Normalize(root.Path), root.Category))
            .OrderByDescending(root => root.Path.Length)
            .ToList();

        var rows = connection.Query<FolderRow>(new CommandDefinition(
            "SELECT Path, SizeBytes, ItemCount FROM ScanItems WHERE ScanSessionId = @sessionId AND ItemType = 'Folder'",
            new { sessionId },
            flags: CommandFlags.None,
            cancellationToken: cancellationToken));

        var results = new List<CandidateLocation>();
        foreach (var row in rows)
        {
            var root = roots.FirstOrDefault(item =>
                row.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)
                || row.Path.StartsWith(item.Path + "\\", StringComparison.OrdinalIgnoreCase));

            var isIncluded = alwaysInclude.Contains(row.Path);
            if (!isIncluded && root.Path is null)
            {
                continue;
            }

            if (isIncluded || IsNearRoot(row.Path, root.Path!, root.Category))
            {
                results.Add(new CandidateLocation(row.Path, root.Path, root.Category, row.SizeBytes, row.ItemCount, true));
            }
        }

        return results;
    }

    // A root is a candidate only when it is a tool's own folder, never a location like AppData.
    private static bool IsNearRoot(string path, string root, string category)
    {
        if (path.Length == root.Length)
        {
            return category == "Development data";
        }

        var depth = path.AsSpan(root.Length + 1).Count('\\') + 1;
        return depth <= MaxDepth;
    }

    private sealed class FolderRow
    {
        public string Path { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public long ItemCount { get; set; }
    }
}
