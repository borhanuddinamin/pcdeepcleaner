using System.Text.Json;
using WCM.Application.Interfaces;
using WCM.Infrastructure.Discovery;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Safety;

public class RuleCatalogueTests
{
    private static string ShippedPath => Path.Combine(RepositoryRoot.Path, "Rules", "application-rules.json");

    [Fact]
    public void Every_shipped_rule_is_present_with_cleanup_disabled()
    {
        using var catalogue = JsonDocument.Parse(File.ReadAllText(ShippedPath));
        var rules = catalogue.RootElement.GetProperty("rules").EnumerateArray().ToList();

        Assert.NotEmpty(rules);
        Assert.All(rules, rule => Assert.False(rule.GetProperty("cleanupAllowed").GetBoolean()));
    }

    [Theory]
    [InlineData(".nuget")]
    [InlineData(".npm")]
    [InlineData(".gradle")]
    [InlineData(".docker")]
    [InlineData(".vscode")]
    [InlineData(".cursor")]
    public async Task No_rule_targets_a_whole_tool_folder(string folder)
    {
        var rules = await new RuleCatalog(ShippedPath, new MemorySettings()).LoadAsync(CancellationToken.None);

        Assert.DoesNotContain(rules, rule => rule.Target.EndsWith("\\" + folder, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Rules_start_off_and_your_choice_persists()
    {
        var settings = new MemorySettings();
        var catalog = new RuleCatalog(ShippedPath, settings);
        Assert.All(await catalog.LoadAsync(CancellationToken.None), rule => Assert.False(rule.CleanupAllowed));

        await catalog.SetEnabledAsync("vscode-logs", true, CancellationToken.None);

        var enabled = (await new RuleCatalog(ShippedPath, settings).LoadAsync(CancellationToken.None)).Where(rule => rule.CleanupAllowed);
        Assert.Equal("vscode-logs", Assert.Single(enabled).RuleId);
    }

    [Theory]
    [InlineData("""{ "catalogueVersion": "1", "rules": [ { "ruleId": "a", "surprise": 1 } ] }""")]
    [InlineData("{ broken")]
    public async Task Invalid_rule_files_fail_closed(string content)
    {
        using var fixture = new SyntheticDataFixture();
        var path = fixture.AddFile("rules.json", content);

        await Assert.ThrowsAsync<InvalidDataException>(() => new RuleCatalog(path, new MemorySettings()).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Rule_enabled_in_its_file_is_rejected()
    {
        using var fixture = new SyntheticDataFixture();
        var text = File.ReadAllText(ShippedPath).Replace("\"cleanupAllowed\": false", "\"cleanupAllowed\": true", StringComparison.Ordinal);
        var path = fixture.AddFile("rules.json", text);

        await Assert.ThrowsAsync<InvalidDataException>(() => new RuleCatalog(path, new MemorySettings()).LoadAsync(CancellationToken.None));
    }

    private sealed class MemorySettings : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
    }
}
