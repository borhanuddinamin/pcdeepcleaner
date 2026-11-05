namespace WCM.Application.DTOs;

public sealed record ScanRoot(string Path, string Category, string Policy, bool Enabled = true);

public sealed record ScanExclusion(string Path, string Reason);

public sealed record ScanConfiguration(
    IReadOnlyList<ScanRoot> Roots,
    IReadOnlyList<ScanExclusion> Exclusions,
    int MaxConcurrency,
    int BatchSize = 2000);

public sealed record ScanProgress(long Files, long Folders, long Errors, long Bytes, string? CurrentPath);

public sealed record ScanErrorRecord(string Path, string Kind, string Message);

public static class ScanStatus
{
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string CompletedWithErrors = "CompletedWithErrors";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";

    /// <summary>The app stopped before the scan finished. Its totals are not reliable.</summary>
    public const string Interrupted = "Interrupted";
}

public static class ScanItemType
{
    public const string File = "File";
    public const string Folder = "Folder";

    /// <summary>A symbolic link, junction or other reparse point. Its target is not read.</summary>
    public const string Link = "Link";

    public const string Excluded = "Excluded";
}

/// <summary>Totals are complete only when <see cref="IsComplete"/> is true.</summary>
public sealed record ScanResult(
    long SessionId,
    string Status,
    long Files,
    long Folders,
    long Errors,
    long Bytes,
    long LinksSkipped,
    long ItemsExcluded,
    int MissingRoots,
    string? FailureReason)
{
    public bool IsComplete => Status == ScanStatus.Completed;
}
