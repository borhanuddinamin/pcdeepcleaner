using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Policies;

namespace WCM.CleanupEngine.Cache;

/// <summary>Lists only what an explicit rule names. Nothing is classified as cache by its name.</summary>
public sealed class CacheAnalyzer(IRuleCatalog rules, ICleanupRepository repository, ProtectedPathPolicy policy) : ICacheAnalyzer
{
    public Task<IReadOnlyList<CacheCandidate>> AnalyzeAsync(CancellationToken cancellationToken) =>
        Task.Run(() => AnalyzeCoreAsync(cancellationToken), CancellationToken.None);

    private async Task<IReadOnlyList<CacheCandidate>> AnalyzeCoreAsync(CancellationToken cancellationToken)
    {
        var results = new List<CacheCandidate>();
        foreach (var rule in (await rules.LoadAsync(cancellationToken)).Where(rule => rule.Target != "*"))
        {
            var rows = await repository.GetScanItemsUnderAsync(rule.Target, rule.Scope == "Children", cancellationToken);
            foreach (var row in rows)
            {
                var protectedReason = policy.Check(row.Path);
                var reason = protectedReason ?? (rule.CleanupAllowed ? "Eligible for preview. Fresh checks run before anything moves." : "This rule is off. Enable it in Settings after reviewing it.");
                results.Add(new CacheCandidate(
                    rule.RuleId, rule.ApplicationKey, rule.Category, rule.DataType, row.Path, row.SizeBytes, row.ItemCount, row.LastModifiedAt,
                    rule.RequiresApplicationClosed, rule.RebuildCost, rule.SensitiveWarning, rule.CleanupAllowed,
                    rule.CleanupAllowed && protectedReason is null, reason));
            }
        }

        return results.OrderByDescending(item => item.SizeBytes).ToList();
    }
}
