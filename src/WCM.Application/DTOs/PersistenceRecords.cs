namespace WCM.Application.DTOs;

public sealed record ApplicationRecord(
    string AppKey,
    string Name,
    string? Publisher,
    string? Version,
    string? InstallLocation,
    string? ExecutablePath,
    string DetectionSource,
    string Status,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastDetectedAt,
    string? ReviewReason = null);

public sealed record ScanSessionRecord(
    long Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    long FilesScanned,
    long FoldersScanned,
    long Errors,
    long TotalBytes = 0,
    long LinksSkipped = 0);

public sealed record ScanItemRecord(
    string Path,
    string ItemType,
    long SizeBytes,
    DateTimeOffset? LastModifiedAt,
    bool IsLocked,
    bool IsProtected,
    long ItemCount = 0,
    int Attributes = 0,
    DateTimeOffset? CreatedAt = null,
    bool IsPartial = false);

public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string Action,
    string? TargetPath,
    string Result,
    string? Details);
