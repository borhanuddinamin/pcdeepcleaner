using System.Text.Json;
using WCM.Application.Interfaces;

namespace WCM.Infrastructure.Windows;

public sealed class DataDirectoryService : IDataDirectoryService
{
    private const string AppFolder = "WindowsCleanupManager";

    private readonly string _settingsDirectory;
    private readonly string _settingsFile;
    private readonly string _executableDirectory;
    private readonly IReadOnlyList<string> _protectedRoots;

    public DataDirectoryService(
        string settingsDirectory,
        string executableDirectory,
        IReadOnlyList<string> protectedRoots)
    {
        _settingsDirectory = settingsDirectory;
        _settingsFile = Path.Combine(settingsDirectory, "bootstrap.json");
        _executableDirectory = executableDirectory;
        _protectedRoots = protectedRoots;
    }

    public bool IsPortable => File.Exists(Path.Combine(_executableDirectory, "portable.marker"));

    public bool IsFirstRun => !IsPortable && !File.Exists(_settingsFile);

    public string DataRoot =>
        IsPortable ? _executableDirectory : ReadSavedRoot() ?? _settingsDirectory;

    public string LogsDirectory => Path.Combine(DataRoot, "Logs");

    public static DataDirectoryService CreateDefault()
    {
        var protectedRoots = new[]
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
            }
            .Select(Environment.GetFolderPath)
            .Where(path => path.Length > 0)
            .ToList();

        return new DataDirectoryService(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder),
            AppContext.BaseDirectory,
            protectedRoots);
    }

    public void SaveDataRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Enter a full path, such as D:\\WCM.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (_protectedRoots.Any(root => IsSameOrInside(fullPath, root)))
        {
            throw new ArgumentException("Windows and Program Files folders can't hold WCM data.", nameof(path));
        }

        Directory.CreateDirectory(fullPath);
        Directory.CreateDirectory(_settingsDirectory);
        File.WriteAllText(_settingsFile, JsonSerializer.Serialize(new BootstrapSettings(fullPath)));
    }

    private static bool IsSameOrInside(string path, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return path.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private string? ReadSavedRoot()
    {
        if (!File.Exists(_settingsFile))
        {
            return null;
        }

        try
        {
            var root = JsonSerializer.Deserialize<BootstrapSettings>(File.ReadAllText(_settingsFile))?.DataRoot;
            if (root is not null && Path.IsPathFullyQualified(root))
            {
                return root;
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Settings file is unreadable: {_settingsFile}", ex);
        }

        throw new InvalidDataException($"Settings file has no valid data root: {_settingsFile}");
    }

    private sealed record BootstrapSettings(string DataRoot);
}
