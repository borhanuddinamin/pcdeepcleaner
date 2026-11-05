using System.Text.Json;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Safety;

public class RuleCatalogueTests
{
    [Fact]
    public void Every_rule_is_present_with_cleanup_disabled()
    {
        var path = Path.Combine(RepositoryRoot.Path, "Rules", "application-rules.json");
        using var catalogue = JsonDocument.Parse(File.ReadAllText(path));
        var rules = catalogue.RootElement.GetProperty("rules").EnumerateArray().ToList();

        Assert.NotEmpty(rules);
        Assert.All(rules, rule => Assert.False(rule.GetProperty("cleanupAllowed").GetBoolean()));
    }
}
