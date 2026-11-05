namespace WCM.Tests.Fixtures;

/// <summary>
/// Development-only synthetic file tree, always under the system temp directory.
/// Paths that would escape the fixture root are rejected.
/// </summary>
public sealed class SyntheticDataFixture : IDisposable
{
    public SyntheticDataFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "WCM.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string AddFile(string relativePath, string content = "synthetic")
    {
        var fullPath = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!fullPath.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Path escapes the fixture root.", nameof(relativePath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
