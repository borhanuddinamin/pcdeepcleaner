using System.Globalization;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Policies;

namespace WCM.CleanupEngine.Orphans;

public sealed record OrphanOptions(int UnconfirmedMinimumIdleDays = 180, long UnconfirmedMinimumBytes = 1024 * 1024);

/// <summary>Finds leftovers for review. Every result stays blocked until you approve it and a rule allows it.</summary>
public sealed class OrphanService(
    ICleanupRepository repository,
    IAuditRepository audit,
    ProtectedPathPolicy policy,
    OrphanOptions options) : IOrphanService
{
    public Task<IReadOnlyList<OrphanCandidate>> DetectAsync(CancellationToken cancellationToken) =>
        Task.Run(() => DetectCoreAsync(cancellationToken), CancellationToken.None);

    public Task<IReadOnlyList<OrphanCandidate>> GetAsync(CancellationToken cancellationToken) =>
        Task.Run(() => repository.GetOrphansAsync(cancellationToken), CancellationToken.None);

    public Task SetDecisionAsync(long id, string decision, CancellationToken cancellationToken)
    {
        if (decision is not (CandidateDecision.Pending or CandidateDecision.Approved or CandidateDecision.Kept))
        {
            throw new ArgumentException("The decision isn't recognized.", nameof(decision));
        }

        return Task.Run(
            async () =>
            {
                await repository.SetOrphanDecisionAsync(id, decision, cancellationToken);
                await audit.AppendAsync(new AuditEntry(DateTimeOffset.UtcNow, "OrphanDecision", null, decision, $"Candidate {id}"), cancellationToken);
            },
            CancellationToken.None);
    }

    /// <summary>
    /// History alone is not proof. Confirmed needs a reliable earlier detection, strong evidence for the
    /// folder, and an install location that is really gone.
    /// </summary>
    public static string Classify(OrphanSource source, bool installLocationExists)
    {
        var onlyShortcut = source.DetectionSource == "StartMenu";
        return source.InstalledSnapshots > 0 && !onlyShortcut && source.HasStrongEvidence && source.InstallLocation is not null && !installLocationExists
            ? OrphanClassification.ConfirmedByHistory
            : OrphanClassification.Potential;
    }

    private async Task<IReadOnlyList<OrphanCandidate>> DetectCoreAsync(CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, OrphanCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in await repository.GetNotDetectedMappingsAsync(cancellationToken))
        {
            if (!await IsOwnedByDetectedAppAsync(source.Path, cancellationToken) && policy.Check(source.Path) is null)
            {
                var classification = Classify(source, source.InstallLocation is not null && Directory.Exists(source.InstallLocation));
                results[source.Path] = new OrphanCandidate
                {
                    AppKey = source.AppKey,
                    ApplicationName = source.Name,
                    Path = source.Path,
                    Classification = classification,
                    SizeBytes = source.SizeBytes,
                    LastDetectedAt = source.LastDetectedAt,
                    Evidence = $"Not found in the latest application scan. Earlier detections: {source.InstalledSnapshots}. {source.Evidence}".Trim(),
                };
            }
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.UnconfirmedMinimumIdleDays);
        foreach (var folder in await repository.GetUnknownFoldersAsync(cancellationToken))
        {
            if (!results.ContainsKey(folder.Path)
                && folder.SizeBytes >= options.UnconfirmedMinimumBytes
                && folder.LastModifiedAt is not null
                && DateTimeOffset.Parse(folder.LastModifiedAt, CultureInfo.InvariantCulture) < cutoff
                && policy.Check(folder.Path) is null
                && !await IsOwnedByDetectedAppAsync(folder.Path, cancellationToken))
            {
                results[folder.Path] = new OrphanCandidate
                {
                    Path = folder.Path,
                    Classification = OrphanClassification.Unconfirmed,
                    SizeBytes = folder.SizeBytes,
                    Evidence = $"No application is linked to this folder, and it hasn't changed since {folder.LastModifiedAt}. This doesn't prove it is unused.",
                };
            }
        }

        await repository.SaveOrphansAsync(results.Values.ToList(), cancellationToken);
        await audit.AppendAsync(new AuditEntry(DateTimeOffset.UtcNow, "OrphanDetection", null, "Completed", $"{results.Count} candidates"), cancellationToken);
        return await repository.GetOrphansAsync(cancellationToken);
    }

    private async Task<bool> IsOwnedByDetectedAppAsync(string path, CancellationToken cancellationToken) =>
        (await repository.GetOwnersAsync(path, cancellationToken)).Any(owner =>
            owner.AppStatus != "NotDetected" && owner.State is not (OwnershipState.UserRejectedMapping or OwnershipState.UnknownPreserved));
}
