using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Enums;
using WCM.Domain.Policies;

namespace WCM.Application.Services;

public sealed class OwnershipService(
    IApplicationRepository applications,
    IMappingRepository mappings,
    ICandidateLocationReader candidates,
    IScanConfigurationStore settings,
    ISignatureCatalog signatures,
    IAuditRepository audit) : IOwnershipService
{
    // Task.Run keeps mapping and saving off the UI thread.
    public Task<MappingResult> MapAsync(CancellationToken cancellationToken) =>
        Task.Run(() => MapCoreAsync(cancellationToken), CancellationToken.None);

    public Task<IReadOnlyList<LocationMapping>> GetLocationsAsync(string appKey, CancellationToken cancellationToken) =>
        Task.Run(() => mappings.GetLocationsAsync(appKey, cancellationToken), CancellationToken.None);

    public Task SetDecisionAsync(long mappingId, string decision, CancellationToken cancellationToken)
    {
        if (decision is not (UserDecision.Confirmed or UserDecision.Rejected))
        {
            throw new ArgumentException("The decision isn't recognized.", nameof(decision));
        }

        return Task.Run(
            async () =>
            {
                var now = DateTimeOffset.UtcNow;
                await mappings.SetDecisionAsync(mappingId, decision, now, cancellationToken);
                await audit.AppendAsync(new AuditEntry(now, "MappingDecision", null, decision, $"Mapping {mappingId}"), cancellationToken);
            },
            CancellationToken.None);
    }

    private async Task<MappingResult> MapCoreAsync(CancellationToken cancellationToken)
    {
        var known = await applications.GetAllAsync(cancellationToken);
        if (known.Count == 0)
        {
            return new MappingResult(false, 0, 0, 0, 0, "Scan for applications first.");
        }

        var catalogue = signatures.Load();
        var configuration = await settings.LoadAsync(cancellationToken);
        var installs = known
            .Where(application => application.Status != ApplicationStatus.NotDetected)
            .Select(application => ApplicationMerger.Normalize(application.InstallLocation))
            .Where(path => path is not null && ApplicationMerger.IsSpecificLocation(path))
            .Select(path => path!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var signaturePaths = catalogue.SelectMany(signature => signature.Paths);
        var include = installs.Concat(signaturePaths).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scanned = await candidates.ReadAsync(configuration, include, cancellationToken);
        if (scanned is null)
        {
            return new MappingResult(false, 0, 0, 0, 0, "Run a storage scan first.");
        }

        // Install folders outside the scanned locations still count, but have no measured size.
        var seen = scanned.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = scanned
            .Concat(installs.Where(path => !seen.Contains(path)).Select(path => new CandidateLocation(path, null, null, 0, 0, false)))
            .ToList();

        var prior = await mappings.GetPriorMappingsAsync(cancellationToken);
        var plan = ApplicationMapper.Build(known, all, catalogue, prior);
        var now = DateTimeOffset.UtcNow;
        await mappings.SavePlanAsync(plan, now, cancellationToken);

        var result = new MappingResult(
            true,
            plan.Locations.Count,
            plan.Mappings.Count,
            plan.Locations.Count(location => location.OwnershipStatus == OwnershipState.SharedProtected),
            plan.Locations.Count(location => location.OwnershipStatus == OwnershipState.UnknownPreserved),
            null);
        await audit.AppendAsync(
            new AuditEntry(now, "OwnershipMapping", null, "Completed", $"{result.Locations} locations, {result.Mappings} mappings"),
            cancellationToken);
        return result;
    }
}
