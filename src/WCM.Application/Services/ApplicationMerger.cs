using System.Text;
using WCM.Application.DTOs;
using WCM.Domain.Enums;

namespace WCM.Application.Services;

/// <summary>
/// Combines source records for the same application. Unclear matches stay separate and are
/// marked for review rather than merged.
/// </summary>
public static class ApplicationMerger
{
    private static readonly string[] SourceOrder = ["Registry", "Appx", "Portable", "StartMenu"];

    private static readonly HashSet<string> GenericFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "program files", "program files (x86)", "programs", "windows", "system32", "syswow64",
        "installer", "appdata", "local", "roaming", "programdata", "users",
    };

    public static IReadOnlyList<MergedApplication> Merge(IEnumerable<ApplicationInfo> records)
    {
        var ordered = records
            .OrderBy(record => SourceRank(record.DetectionSource))
            .ThenBy(record => record.AppKey, StringComparer.Ordinal);
        var groups = new List<Group>();

        foreach (var record in ordered)
        {
            var conflicts = new List<Group>();
            Group? match = null;

            foreach (var group in groups)
            {
                var relation = group.Sources.Select(source => Relate(source, record)).Max();
                if (relation == Relation.Same)
                {
                    match = group;
                    break;
                }

                if (relation == Relation.Conflict)
                {
                    conflicts.Add(group);
                }
            }

            if (match is not null)
            {
                match.Sources.Add(record);
                continue;
            }

            var created = new Group(record);
            groups.Add(created);
            foreach (var conflict in conflicts)
            {
                var reason = $"\"{conflict.Sources[0].Name}\" and \"{record.Name}\" use the same location but may be different applications.";
                conflict.ReviewReason ??= reason;
                created.ReviewReason ??= reason;
            }
        }

        return groups.Select(ToMerged).ToList();
    }

    private static MergedApplication ToMerged(Group group)
    {
        var first = group.Sources[0];
        var application = first with
        {
            Publisher = group.Sources.Select(s => s.Publisher).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            Version = group.Sources.Select(s => s.Version).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            InstallLocation = group.Sources.Select(s => s.InstallLocation).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            ExecutablePath = group.Sources.Select(s => s.ExecutablePath).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            Architecture = group.Sources.Select(s => s.Architecture).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            DetectionSource = string.Join(", ", group.Sources.Select(s => s.DetectionSource).Distinct(StringComparer.Ordinal)),
            Status = group.ReviewReason is null ? CombinedStatus(group.Sources) : ApplicationStatus.NeedsReview,
        };
        return new MergedApplication(application, group.ReviewReason, group.Sources);
    }

    private static string CombinedStatus(List<ApplicationInfo> sources)
    {
        foreach (var status in new[] { ApplicationStatus.Installed, ApplicationStatus.Portable, ApplicationStatus.Unverified })
        {
            if (sources.Any(source => source.Status == status))
            {
                return status;
            }
        }

        return sources[0].Status;
    }

    private static Relation Relate(ApplicationInfo a, ApplicationInfo b)
    {
        if (a.AppKey.Equals(b.AppKey, StringComparison.OrdinalIgnoreCase))
        {
            return Relation.Same;
        }

        var publisherConflict = !string.IsNullOrWhiteSpace(a.Publisher)
            && !string.IsNullOrWhiteSpace(b.Publisher)
            && !Compatible(a.Publisher, b.Publisher);
        var exactPlace = SamePath(a.ExecutablePath, b.ExecutablePath) || SameLocation(a.InstallLocation, b.InstallLocation);
        var overlap = exactPlace || Inside(a.ExecutablePath, b.InstallLocation) || Inside(b.ExecutablePath, a.InstallLocation);

        if (overlap)
        {
            // A shortcut pointing into an application's folder belongs to it, whatever its label says.
            var isShortcut = a.DetectionSource == "StartMenu" || b.DetectionSource == "StartMenu";
            if (isShortcut && SamePath(a.ExecutablePath, b.ExecutablePath))
            {
                // File details can name a different publisher than the installer did.
                return Relation.Same;
            }

            if (!publisherConflict && (isShortcut || Compatible(a.Name, b.Name)))
            {
                return Relation.Same;
            }

            return exactPlace ? Relation.Conflict : Relation.None;
        }

        var bothPublishersKnown = !string.IsNullOrWhiteSpace(a.Publisher) && !string.IsNullOrWhiteSpace(b.Publisher);
        return bothPublishersKnown && !publisherConflict && Compact(a.Name) == Compact(b.Name)
            ? Relation.Same
            : Relation.None;
    }

    private static int SourceRank(string source)
    {
        var rank = Array.IndexOf(SourceOrder, source);
        return rank < 0 ? SourceOrder.Length : rank;
    }

    private static bool Compatible(string a, string b)
    {
        var left = Compact(a);
        var right = Compact(b);
        return left.Length >= 3 && right.Length >= 3
            && (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal));
    }

    internal static string Compact(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    internal static string? Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : Path.TrimEndingDirectorySeparator(path.Trim().Replace('/', '\\'));

    private static bool SamePath(string? a, string? b)
    {
        var left = Normalize(a);
        return left is not null && left.Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameLocation(string? a, string? b) => IsSpecificLocation(a) && SamePath(a, b);

    private static bool Inside(string? file, string? location)
    {
        var path = Normalize(file);
        var root = Normalize(location);
        return path is not null && root is not null && IsSpecificLocation(root)
            && path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    // Folders such as Program Files hold unrelated applications, so they prove nothing.
    internal static bool IsSpecificLocation(string? location)
    {
        var path = Normalize(location);
        if (path is null || Path.GetPathRoot(path)?.TrimEnd('\\').Equals(path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        return !GenericFolders.Contains(Path.GetFileName(path));
    }

    private enum Relation
    {
        None,
        Conflict,
        Same,
    }

    private sealed class Group(ApplicationInfo first)
    {
        public List<ApplicationInfo> Sources { get; } = [first];

        public string? ReviewReason { get; set; }
    }
}
