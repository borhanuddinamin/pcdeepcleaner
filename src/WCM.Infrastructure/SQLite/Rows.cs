namespace WCM.Infrastructure.SQLite;

// Dapper targets: timestamps are stored as ISO-8601 UTC text.
internal sealed class AppliedMigrationRow
{
    public long Version { get; set; }

    public string Checksum { get; set; } = string.Empty;
}

internal sealed class ApplicationRow
{
    public string AppKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Publisher { get; set; }

    public string? Version { get; set; }

    public string? InstallLocation { get; set; }

    public string? ExecutablePath { get; set; }

    public string DetectionSource { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string FirstDetectedAt { get; set; } = string.Empty;

    public string LastDetectedAt { get; set; } = string.Empty;

    public string? ReviewReason { get; set; }
}

internal sealed class ScanSessionRow
{
    public long Id { get; set; }

    public string StartedAt { get; set; } = string.Empty;

    public string? CompletedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public long FilesScanned { get; set; }

    public long FoldersScanned { get; set; }

    public long Errors { get; set; }
}

internal sealed class AuditRow
{
    public string Timestamp { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? TargetPath { get; set; }

    public string Result { get; set; } = string.Empty;

    public string? Details { get; set; }
}
