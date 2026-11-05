using System.Text.RegularExpressions;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Safety;

public partial class NoCleanupCodeTests
{
    [Fact]
    public void Only_FileOperations_may_move_or_delete_files_or_write_the_registry()
    {
        var src = Path.Combine(RepositoryRoot.Path, "src");
        var violations = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(IsProductionSource)
            .Where(file => Path.GetFileName(file) != "FileOperations.cs")
            .Where(file => ForbiddenCall().IsMatch(File.ReadAllText(file)))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void FileOperations_never_overwrites_and_exists_once()
    {
        var src = Path.Combine(RepositoryRoot.Path, "src");
        var file = Assert.Single(Directory.EnumerateFiles(src, "FileOperations.cs", SearchOption.AllDirectories), IsProductionSource);
        var text = File.ReadAllText(file);

        Assert.DoesNotContain("overwrite: true", text, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("WCM.CleanupEngine", "Quarantine"), file, StringComparison.Ordinal);
        Assert.DoesNotMatch(RegistryWrite(), text);
    }

    [Theory]
    [InlineData("File.Delete(path)")]
    [InlineData("Directory.Move(a, b)")]
    [InlineData("key.DeleteSubKeyTree(name)")]
    [InlineData("new FileInfo(p).MoveTo(q)")]
    public void Guard_detects_forbidden_calls(string code) => Assert.Matches(ForbiddenCall(), code);

    private static bool IsProductionSource(string file)
    {
        var parts = file.Split(Path.DirectorySeparatorChar);
        return !parts.Contains("WCM.Tests") && !parts.Contains("bin") && !parts.Contains("obj");
    }

    [GeneratedRegex(@"\b(File|Directory)\.(Delete|Move|Replace)\s*\(|\.(MoveTo|Delete)\s*\(|\b(SetValue|DeleteValue|DeleteSubKey\w*|CreateSubKey)\s*\(")]
    private static partial Regex ForbiddenCall();

    [GeneratedRegex(@"\b(SetValue|DeleteValue|DeleteSubKey\w*|CreateSubKey)\s*\(")]
    private static partial Regex RegistryWrite();
}
