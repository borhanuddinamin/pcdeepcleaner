using System.Text.RegularExpressions;
using WCM.Application.DTOs;
using WCM.Domain.Enums;
using WCM.Domain.Policies;

namespace WCM.Application.Services;

/// <summary>Builds evidence-based mappings. It only classifies: everything it produces stays protected.</summary>
public static partial class ApplicationMapper
{
    private static readonly HashSet<string> GenericSegments = new(StringComparer.Ordinal)
    {
        "cache", "caches", "temp", "tmp", "logs", "log", "data", "common", "shared", "app", "apps", "application",
        "update", "updater", "programs", "packages", "windows", "microsoft", "settings", "config", "user", "users",
        "local", "roaming", "bin", "lib", "tools", "client", "service", "services",
    };

    public static MappingPlan Build(
        IReadOnlyList<ApplicationRecord> applications,
        IReadOnlyList<CandidateLocation> candidates,
        IReadOnlyList<KnownApplicationSignature> signatures,
        IReadOnlyList<PriorMapping> prior)
    {
        var priorByKey = prior
            .GroupBy(item => Key(item.AppKey, item.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var profiles = applications.Select(application => new Profile(application, SignificantName(application.Name))).ToList();
        var locations = new List<MappedLocation>(candidates.Count);
        var mappings = new List<MappedOwnership>();

        foreach (var candidate in candidates)
        {
            var found = new List<Found>();
            foreach (var profile in profiles)
            {
                priorByKey.TryGetValue(Key(profile.Application.AppKey, candidate.Path), out var earlier);
                var evidence = Collect(profile, candidate, signatures, earlier);
                if (evidence.Count > 0)
                {
                    found.Add(new Found(profile.Application, evidence, earlier?.Decision));
                }
            }

            var owners = found.Where(IsActiveOwner).ToList();
            foreach (var item in found)
            {
                var isOwner = owners.Contains(item);
                if (isOwner && owners.Count > 1)
                {
                    item.Evidence.Add(OwnershipScorer.Create(EvidenceType.SharedActive, $"{owners.Count} detected applications have evidence for this location."));
                }
                else if (!isOwner && owners.Count > 0 && item.Decision != UserDecision.Rejected)
                {
                    item.Evidence.Add(OwnershipScorer.Create(EvidenceType.Conflict, $"\"{owners[0].Application.Name}\" has stronger evidence for this location."));
                }

                mappings.Add(new MappedOwnership(
                    item.Application.AppKey,
                    candidate.Path,
                    OwnershipScorer.Score(item.Evidence),
                    StateOf(item, isOwner, owners.Count),
                    item.Evidence));
            }

            var status = owners.Count switch
            {
                0 => OwnershipState.UnknownPreserved,
                1 => OwnershipState.InstalledProtected,
                _ => OwnershipState.SharedProtected,
            };
            locations.Add(new MappedLocation(candidate, DataTypeOf(candidate), status));
        }

        return new MappingPlan(locations, mappings);
    }

    /// <summary>Removes versions and bracketed text: "7-Zip 26.03 (x64)" becomes "7zip".</summary>
    public static string SignificantName(string name)
    {
        var withoutBrackets = Bracketed().Replace(name, " ");
        var words = withoutBrackets
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !VersionWord().IsMatch(word));
        return ApplicationMerger.Compact(string.Join(' ', words));
    }

    private static bool IsActiveOwner(Found item) =>
        item.Decision != UserDecision.Rejected
        && item.Application.Status != ApplicationStatus.NotDetected
        && (item.Decision == UserDecision.Confirmed || OwnershipScorer.IsStrong(item.Evidence));

    private static string StateOf(Found item, bool isOwner, int ownerCount) => item.Decision switch
    {
        UserDecision.Rejected => OwnershipState.UserRejectedMapping,
        UserDecision.Confirmed => OwnershipState.UserConfirmedOwnership,
        _ when isOwner && ownerCount > 1 => OwnershipState.SharedProtected,
        _ when isOwner => OwnershipState.InstalledProtected,
        _ => OwnershipState.UnknownPreserved,
    };

    private static List<EvidenceItem> Collect(
        Profile profile,
        CandidateLocation candidate,
        IReadOnlyList<KnownApplicationSignature> signatures,
        PriorMapping? earlier)
    {
        var application = profile.Application;
        var evidence = new List<EvidenceItem>();
        var path = candidate.Path;

        foreach (var signature in signatures)
        {
            if (!Contains(application.Name, signature.NameContains)
                || !signature.Paths.Any(known => known.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var publisherMatches = signature.PublisherContains is not null
                && application.Publisher is not null
                && Contains(application.Publisher, signature.PublisherContains);
            evidence.Add(publisherMatches
                ? OwnershipScorer.Create(EvidenceType.ExactSignature, $"Signature {signature.SignatureId} {signature.SignatureVersion} lists this path, and the name and publisher match.")
                : OwnershipScorer.Create(EvidenceType.KnownPath, $"Signature {signature.SignatureId} {signature.SignatureVersion} lists this path for this application name."));
        }

        if (application.ExecutablePath is not null
            && path.Equals(Path.GetDirectoryName(application.ExecutablePath), StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add(OwnershipScorer.Create(EvidenceType.ExecutableMetadata, $"The application's program is here: {application.ExecutablePath}"));
        }

        var install = ApplicationMerger.Normalize(application.InstallLocation);
        if (install is not null && ApplicationMerger.IsSpecificLocation(install) && Related(path, install))
        {
            evidence.Add(OwnershipScorer.Create(EvidenceType.InstallPath, $"Related to the install location {install}."));
        }

        var segments = RelativeSegments(candidate);
        var last = segments.Length == 0 ? string.Empty : ApplicationMerger.Compact(segments[^1]);
        var nameMatches = profile.SignificantName.Length >= 3 && last == profile.SignificantName && !GenericSegments.Contains(last);
        if (nameMatches)
        {
            evidence.Add(OwnershipScorer.Create(EvidenceType.FolderName, $"The folder name matches \"{application.Name}\". This is weak evidence."));

            var publisher = application.Publisher is null ? string.Empty : ApplicationMerger.Compact(application.Publisher);
            var parent = segments[..^1].FirstOrDefault(segment =>
            {
                var compact = ApplicationMerger.Compact(segment);
                return compact.Length >= 4 && publisher.Contains(compact, StringComparison.Ordinal);
            });
            if (parent is not null)
            {
                evidence.Add(OwnershipScorer.Create(EvidenceType.Publisher, $"The parent folder \"{parent}\" matches the publisher \"{application.Publisher}\"."));
            }
        }

        if (earlier is not null)
        {
            evidence.Add(OwnershipScorer.Create(EvidenceType.Historical, "An earlier mapping linked this application to this location."));
            if (earlier.Decision is not null)
            {
                evidence.Add(OwnershipScorer.Create(EvidenceType.UserDecision, $"You marked this link as {earlier.Decision.ToLowerInvariant()}."));
            }
        }

        return evidence;
    }

    private static bool Related(string path, string other) =>
        path.Equals(other, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(other + "\\", StringComparison.OrdinalIgnoreCase);

    private static string[] RelativeSegments(CandidateLocation candidate)
    {
        if (candidate.RootPath is null || candidate.Path.Equals(candidate.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return [Path.GetFileName(candidate.Path)];
        }

        return candidate.Path[(candidate.RootPath.Length + 1)..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
    }

    // Deliberately never returns a cache type. Only explicit rules (Phase 7) may do that.
    private static string DataTypeOf(CandidateLocation candidate) => candidate.RootCategory switch
    {
        null or "Program Files" or "Program Files x86" => "ApplicationInstall",
        "User temporary files" or "Windows temporary files" => "TemporaryFiles",
        "Development data" => "DevelopmentData",
        _ => "ApplicationData",
    };

    private static bool Contains(string value, string part) => value.Contains(part, StringComparison.OrdinalIgnoreCase);

    private static string Key(string appKey, string path) => appKey + "|" + path;

    [GeneratedRegex(@"\(.*?\)")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"^(v?\d+([.\-]\d+)*|x64|x86|arm64|\d+-bit)$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionWord();

    private sealed record Profile(ApplicationRecord Application, string SignificantName);

    private sealed record Found(ApplicationRecord Application, List<EvidenceItem> Evidence, string? Decision);
}
