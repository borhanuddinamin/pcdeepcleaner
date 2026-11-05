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
    long Errors);

public sealed record ScanItemRecord(
    string Path,
    string ItemType,
    long SizeBytes,
    DateTimeOffset? LastModifiedAt,
    bool IsLocked,
    bool IsProtected);

public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string Action,
    string? TargetPath,
    string Result,
    string? Details);
