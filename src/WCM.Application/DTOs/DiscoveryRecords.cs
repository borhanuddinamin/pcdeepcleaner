namespace WCM.Application.DTOs;

public sealed record ApplicationInfo(
    string AppKey,
    string Name,
    string? Publisher,
    string? Version,
    string? InstallLocation,
    string? ExecutablePath,
    string DetectionSource,
    string Status,
    string? Architecture = null);

/// <summary>One application with every source record that supports it.</summary>
public sealed record MergedApplication(
    ApplicationInfo Application,
    string? ReviewReason,
    IReadOnlyList<ApplicationInfo> Sources);

public sealed record DiscoveryResult(
    int ApplicationCount,
    int SourceRecordCount,
    bool IsComplete,
    bool WasSaved,
    IReadOnlyList<string> Failures);
