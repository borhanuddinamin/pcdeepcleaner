using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Infrastructure.SQLite;

namespace WCM.Infrastructure.FileSystem;

public sealed class ReportService(SqliteConnectionFactory connections) : IReportService
{
    private static readonly Dictionary<string, string> Queries = new(StringComparer.Ordinal)
    {
        [ReportKind.Applications] = "SELECT Name, Publisher, Version, Status, DetectionSource, InstallLocation, ExecutablePath, ReviewReason, FirstDetectedAt, LastDetectedAt FROM Applications ORDER BY Name",
        [ReportKind.StorageScan] = "SELECT Id, StartedAt, CompletedAt, Status, FilesScanned, FoldersScanned, Errors, TotalBytes, LinksSkipped FROM ScanSessions ORDER BY Id DESC",
        [ReportKind.OwnershipEvidence] = "SELECT a.Name AS Application, s.Path, m.ConfidenceScore, m.OwnershipStatus, e.EvidenceType, e.Description, e.Score FROM OwnershipEvidence e JOIN ApplicationStorageMappings m ON m.Id = e.MappingId JOIN Applications a ON a.Id = m.ApplicationId JOIN StorageLocations s ON s.Id = m.StorageLocationId ORDER BY a.Name, s.Path",
        [ReportKind.OrphanCandidates] = "SELECT ApplicationName, Path, Classification, Decision, SizeBytes, LastDetectedAt, Evidence FROM OrphanCandidates ORDER BY SizeBytes DESC",
        [ReportKind.CleanupPreview] = "SELECT CleanupPlanId, Path, SourceKind, RuleId, SizeBytes, ItemCount, ValidationStatus, BlockReason FROM CleanupPlanItems ORDER BY CleanupPlanId DESC",
        [ReportKind.QuarantineHistory] = "SELECT i.OperationId, i.AppKey, i.OriginalPath, i.QuarantinePath, i.SizeBytes, i.Status, i.VerificationHash, i.CreatedAt, i.RestoredPath, i.Message FROM QuarantineItems i ORDER BY i.Id DESC",
        [ReportKind.DeletionHistory] = "SELECT d.QuarantineItemId, i.OriginalPath, d.Bytes, d.DeletedAt, d.Status FROM DeletionOperations d JOIN QuarantineItems i ON i.Id = d.QuarantineItemId ORDER BY d.Id DESC",
        [ReportKind.RecoveredSpace] = "SELECT p.Id AS PlanId, p.EstimatedBytes, COALESCE((SELECT SUM(d.Bytes) FROM DeletionOperations d JOIN QuarantineItems i ON i.Id = d.QuarantineItemId JOIN QuarantineOperations o ON o.Id = i.OperationId WHERE o.CleanupPlanId = p.Id AND d.Status = 'PermanentlyDeleted'), 0) AS ActualFreedBytes FROM CleanupPlans p ORDER BY p.Id DESC",
    };

    public async Task<int> ExportAsync(string kind, string format, string destinationPath, CancellationToken cancellationToken)
    {
        if (!Queries.TryGetValue(kind, out var sql))
        {
            throw new ArgumentException("The report type isn't recognized.", nameof(kind));
        }

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var reader = await connection.ExecuteReaderAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var values = new object?[reader.FieldCount];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }

            rows.Add(values);
        }

        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            var objects = rows.Select(row => columns.Select((column, index) => (column, value: row[index])).ToDictionary(pair => pair.column, pair => pair.value));
            await File.WriteAllTextAsync(destinationPath, JsonSerializer.Serialize(objects, Indented), cancellationToken);
        }
        else
        {
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(',', columns.Select(Escape)));
            foreach (var row in rows)
            {
                csv.AppendLine(string.Join(',', row.Select(value => Escape(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)))));
            }

            await File.WriteAllTextAsync(destinationPath, csv.ToString(), new UTF8Encoding(true), cancellationToken);
        }

        return rows.Count;
    }

    // A leading quote stops spreadsheets from running a value that looks like a formula.
    internal static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' && !double.TryParse(text, out _))
        {
            text = "'" + text;
        }

        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}

public sealed class PathNormalizerService : IPathNormalizer
{
    public string Normalize(string path) => PathNormalizer.Normalize(path);
}
