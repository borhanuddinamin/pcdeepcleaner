using WCM.Application.DTOs;

namespace WCM.Application.Interfaces;

public interface IOwnershipService
{
    Task<MappingResult> MapAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LocationMapping>> GetLocationsAsync(string appKey, CancellationToken cancellationToken);

    Task SetDecisionAsync(long mappingId, string decision, CancellationToken cancellationToken);
}

public interface IMappingRepository
{
    Task<IReadOnlyList<PriorMapping>> GetPriorMappingsAsync(CancellationToken cancellationToken);

    Task SavePlanAsync(MappingPlan plan, DateTimeOffset observedAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<LocationMapping>> GetLocationsAsync(string appKey, CancellationToken cancellationToken);

    Task SetDecisionAsync(long mappingId, string decision, DateTimeOffset decidedAt, CancellationToken cancellationToken);
}

public interface ICandidateLocationReader
{
    /// <summary>Reads folders near scan roots, plus <paramref name="alwaysInclude"/>, from the latest finished scan.</summary>
    /// <returns>The candidates, or null when no scan has finished.</returns>
    Task<IReadOnlyList<CandidateLocation>?> ReadAsync(
        ScanConfiguration configuration,
        IReadOnlySet<string> alwaysInclude,
        CancellationToken cancellationToken);
}

public interface ISignatureCatalog
{
    /// <exception cref="InvalidDataException">The catalogue is missing or invalid.</exception>
    IReadOnlyList<KnownApplicationSignature> Load();
}
