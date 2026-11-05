using System.Text.Json;
using System.Text.Json.Serialization;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Infrastructure.FileSystem;

namespace WCM.Infrastructure.Discovery;

/// <summary>Loads the local rules. Shipped rules are off until you enable each one in Settings.</summary>
public sealed class RuleCatalog(string filePath, ISettingsStore settings) : IRuleCatalog
{
    private const string Key = "EnabledCleanupRules";

    private static readonly HashSet<string> Scopes = new(StringComparer.Ordinal) { "Folder", "Children" };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Rules", "application-rules.json");

    public async Task<IReadOnlyList<CleanupRule>> LoadAsync(CancellationToken cancellationToken)
    {
        RuleFile file;
        try
        {
            file = JsonSerializer.Deserialize<RuleFile>(await File.ReadAllTextAsync(filePath, cancellationToken), Options)
                ?? throw new InvalidDataException("The rule file is empty.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"The rule file can't be used: {filePath}", ex);
        }

        var enabled = await EnabledAsync(cancellationToken);
        return file.Rules.Select(rule => Validate(rule) with { CleanupAllowed = enabled.Contains(rule.RuleId) }).ToList();
    }

    public async Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken cancellationToken)
    {
        var current = await EnabledAsync(cancellationToken);
        if (enabled)
        {
            current.Add(ruleId);
        }
        else
        {
            current.Remove(ruleId);
        }

        await settings.SetAsync(Key, JsonSerializer.Serialize(current), cancellationToken);
    }

    private async Task<HashSet<string>> EnabledAsync(CancellationToken cancellationToken)
    {
        var json = await settings.GetAsync(Key, cancellationToken);
        return json is null ? new HashSet<string>(StringComparer.Ordinal) : JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
    }

    private static CleanupRule Validate(CleanupRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.RuleId) || string.IsNullOrWhiteSpace(rule.RuleVersion)
            || string.IsNullOrWhiteSpace(rule.Target) || !Scopes.Contains(rule.Scope) || rule.MinimumAgeDays < 0 || rule.ProcessNames is null)
        {
            throw new InvalidDataException($"Rule \"{rule.RuleId}\" is incomplete.");
        }

        if (rule.CleanupAllowed)
        {
            throw new InvalidDataException($"Rule \"{rule.RuleId}\" is enabled in its file. Rules must ship disabled.");
        }

        if (rule.Target == "*")
        {
            return rule;
        }

        var expanded = Environment.ExpandEnvironmentVariables(rule.Target);
        if (!rule.Target.StartsWith('%') || expanded.Contains('%', StringComparison.Ordinal) || !Path.IsPathFullyQualified(expanded))
        {
            throw new InvalidDataException($"Rule \"{rule.RuleId}\" has an invalid target: {rule.Target}");
        }

        return rule with { Target = PathNormalizer.Normalize(expanded) };
    }

    private sealed record RuleFile(string CatalogueVersion, IReadOnlyList<CleanupRule> Rules);
}
