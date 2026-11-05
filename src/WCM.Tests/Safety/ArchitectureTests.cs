using System.Xml.Linq;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Safety;

// Reads csproj files because compilers drop unused references from assembly metadata.
public class ArchitectureTests
{
    [Theory]
    [InlineData("WCM.Domain")]
    [InlineData("WCM.Application", "WCM.Domain")]
    [InlineData("WCM.Infrastructure", "WCM.Application", "WCM.Domain")]
    [InlineData("WCM.CleanupEngine", "WCM.Application", "WCM.Domain")]
    [InlineData("WCM.Desktop", "WCM.Application")]
    public void Project_references_follow_the_dependency_direction(string project, params string[] expected)
    {
        var actual = ProjectFile(project).Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value))
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Domain_has_no_packages_and_no_windows_or_ui_dependency()
    {
        var domain = ProjectFile("WCM.Domain");

        Assert.Empty(domain.Descendants("PackageReference"));
        Assert.Empty(domain.Descendants("UseWPF"));
        Assert.Equal("net10.0", domain.Descendants("TargetFramework").Single().Value);
    }

    private static XDocument ProjectFile(string project) =>
        XDocument.Load(Path.Combine(RepositoryRoot.Path, "src", project, project + ".csproj"));
}
