using System.Text.RegularExpressions;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Safety;

// Foundation-phase guard. Narrow it to WCM.CleanupEngine when Phase 10 adds quarantine.
public partial class NoCleanupCodeTests
{
    [Fact]
    public void Production_code_contains_no_file_mutation_or_registry_write_calls()
    {
        var src = Path.Combine(RepositoryRoot.Path, "src");
        var violations = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(IsProductionSource)
            .Where(file => ForbiddenCall().IsMatch(File.ReadAllText(file)))
            .ToList();

        Assert.Empty(violations);
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
}
