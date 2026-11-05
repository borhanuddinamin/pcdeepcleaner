using WCM.Application.DTOs;

namespace WCM.Infrastructure.FileSystem;

public static class ScanDefaults
{
    private static readonly string[] DevelopmentFolders =
    [
        ".nuget", ".npm", ".gradle", ".docker", ".vscode", ".cursor", ".claude", ".config", ".cache",
        ".local", ".m2", ".cargo", ".rustup",
    ];

    public static ScanConfiguration Create(string dataRoot)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var roots = new List<ScanRoot>
        {
            new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Local application data", "Scan; classify by rule"),
            new(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Roaming application data", "Scan; protect settings and data"),
            new(Path.Combine(profile, "AppData", "LocalLow"), "LocalLow application data", "Scan; classify by rule"),
            new(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Machine-wide application data", "Scan; strict protection"),
            new(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Program Files", "Discovery and mapping; no cleanup"),
            new(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Program Files x86", "Discovery and mapping; no cleanup"),
            new(Path.GetTempPath(), "User temporary files", "Scan; stale-file rules"),
            new(Path.Combine(windows, "Temp"), "Windows temporary files", "Restricted; needs elevation and dedicated rules", Enabled: false),
        };
        roots.AddRange(DevelopmentFolders.Select(folder =>
            new ScanRoot(Path.Combine(profile, folder), "Development data", "Scan; never disposable by name")));

        return new ScanConfiguration(
            roots.Where(root => !string.IsNullOrWhiteSpace(root.Path)).ToList(),
            [new ScanExclusion(dataRoot, "WCM's own database, logs and quarantine")],
            Math.Clamp(Environment.ProcessorCount / 2, 2, 4));
    }
}
