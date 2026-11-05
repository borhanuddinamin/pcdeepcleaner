using System.Text.RegularExpressions;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Safety;

public partial class RegistryReadOnlyTests
{
    [Fact]
    public void Registry_keys_are_never_opened_for_writing()
    {
        var src = Path.Combine(RepositoryRoot.Path, "src", "WCM.Infrastructure");
        var violations = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file => WritableOpen().IsMatch(File.ReadAllText(file)))
            .ToList();

        Assert.Empty(violations);
    }

    [GeneratedRegex(@"OpenSubKey\s*\([^)]*,")]
    private static partial Regex WritableOpen();
}
