using WCM.Domain.Policies;

namespace WCM.Application.DTOs;

public sealed record KnownApplicationSignature(
    string SignatureId,
    string SignatureVersion,
    string NameContains,
    string? PublisherContains,
    IReadOnlyList<string> Paths);

public sealed record CandidateLocation(
    string Path,
    string? RootPath,
    string? RootCategory,
    long SizeBytes,
    long ItemCount,
    bool IsMeasured);

public sealed record PriorMapping(string AppKey, string Path, string? Decision);

public sealed record MappedOwnership(string AppKey, string Path, int Score, string State, IReadOnlyList<EvidenceItem> Evidence);

public sealed record MappedLocation(CandidateLocation Location, string DataType, string OwnershipStatus);

public sealed record MappingPlan(IReadOnlyList<MappedLocation> Locations, IReadOnlyList<MappedOwnership> Mappings);

public sealed record MappingResult(bool WasSaved, int Locations, int Mappings, int SharedLocations, int UnknownLocations, string? Problem);

public sealed record LocationMapping(
    long MappingId,
    string Path,
    string DataType,
    long SizeBytes,
    long ItemCount,
    int Score,
    string State,
    bool IsProtected,
    IReadOnlyList<EvidenceItem> Evidence)
{
    public string Band => OwnershipScorer.Band(Score);

    public string ProtectionReason => OwnershipPolicy.ExplainProtection(State);
}

public static class UserDecision
{
    public const string Confirmed = "Confirmed";
    public const string Rejected = "Rejected";
}
