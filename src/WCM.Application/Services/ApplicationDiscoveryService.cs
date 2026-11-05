using WCM.Application.DTOs;
using WCM.Application.Interfaces;

namespace WCM.Application.Services;

public sealed class ApplicationDiscoveryService(
    IEnumerable<IApplicationDiscoveryProvider> providers,
    IApplicationRepository applications,
    IAuditRepository audit) : IApplicationDiscoveryService
{
    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        var records = new List<ApplicationInfo>();
        var failures = new List<string>();

        foreach (var provider in providers)
        {
            try
            {
                records.AddRange(await Task.Run(() => provider.DiscoverAsync(cancellationToken), cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"{provider.ProviderName}: {ex.Message}");
            }
        }

        var now = DateTimeOffset.UtcNow;

        // An empty result is more likely a failure than a machine with no applications.
        if (records.Count == 0)
        {
            failures.Add("No application records were found.");
            await WriteAuditAsync(now, "Failed", failures, cancellationToken);
            return new DiscoveryResult(0, 0, false, false, failures);
        }

        var merged = ApplicationMerger.Merge(records);
        var isComplete = failures.Count == 0;
        await applications.SaveInventoryAsync(merged, now, isComplete, cancellationToken);
        await WriteAuditAsync(now, isComplete ? "Completed" : "Partial", failures, cancellationToken);
        return new DiscoveryResult(merged.Count, records.Count, isComplete, true, failures);
    }

    private Task WriteAuditAsync(
        DateTimeOffset now,
        string result,
        List<string> failures,
        CancellationToken cancellationToken) =>
        audit.AppendAsync(
            new AuditEntry(now, "ApplicationDiscovery", null, result, failures.Count == 0 ? null : string.Join("; ", failures)),
            cancellationToken);
}
