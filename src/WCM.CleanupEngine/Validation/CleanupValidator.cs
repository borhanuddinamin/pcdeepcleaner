using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Policies;

namespace WCM.CleanupEngine.Validation;

/// <summary>Read-only. Any doubt blocks the item. Runs at planning and again just before each move.</summary>
public sealed class CleanupValidator(
    IScanConfigurationStore settings,
    IRuleCatalog rules,
    ICleanupRepository repository,
    IPathNormalizer paths,
    IProcessInspector processes,
    ProtectedPathPolicy policy)
{
    public async Task<ValidationOutcome> ValidateAsync(CleanupSelection selection, CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        TreeStats? stats = null;
        var path = selection.Path;

        string normalized;
        try
        {
            normalized = paths.Normalize(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return new ValidationOutcome(["The path isn't valid."], null);
        }

        if (!normalized.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("The path no longer resolves to the same location.");
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return new ValidationOutcome(["The item no longer exists."], null);
        }

        var configuration = await settings.LoadAsync(cancellationToken);
        var root = configuration.Roots
            .Where(item => item.Enabled)
            .Select(item => paths.Normalize(item.Path))
            .Where(item => path.StartsWith(item + "\\", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Length)
            .FirstOrDefault();
        if (root is null)
        {
            reasons.Add("The path is outside the approved scan locations.");
        }
        else if (HasLinkInPath(path, root))
        {
            reasons.Add("The path goes through a link.");
        }

        var protectedReason = policy.Check(normalized);
        if (protectedReason is not null)
        {
            reasons.Add(protectedReason);
        }

        var rule = (await rules.LoadAsync(cancellationToken)).FirstOrDefault(item => item.RuleId == selection.RuleId);
        var isApproved = true;
        var state = OwnershipState.CacheCandidate;
        if (rule is null)
        {
            reasons.Add("The cleanup rule doesn't exist.");
        }
        else if (selection.SourceKind == SourceKind.Orphan)
        {
            state = OwnershipState.PotentialOrphan;
            isApproved = await repository.GetOrphanDecisionAsync(path, cancellationToken) == CandidateDecision.Approved;
            if (rule.Target != "*")
            {
                reasons.Add("This rule doesn't cover leftovers.");
            }
        }
        else if (selection.SourceKind != SourceKind.Cache)
        {
            reasons.Add("The item type isn't recognized.");
        }
        else if (!Covers(rule, path))
        {
            reasons.Add("The rule doesn't cover this path.");
        }

        var owners = await repository.GetOwnersAsync(path, cancellationToken);
        if (selection.SourceKind == SourceKind.Orphan)
        {
            var active = owners.FirstOrDefault(owner => owner.AppStatus != "NotDetected" && owner.State != OwnershipState.UserRejectedMapping && owner.State != OwnershipState.UnknownPreserved);
            if (active is not null)
            {
                reasons.Add($"\"{active.Name}\" is still detected and is linked to {active.Path}.");
            }
        }
        else if (owners.Any(owner => owner.State == OwnershipState.SharedProtected))
        {
            reasons.Add("More than one application uses this location.");
        }

        if (rule is not null)
        {
            foreach (var process in rule.ProcessNames.Where(processes.IsRunning))
            {
                reasons.Add($"{process} must be closed first.");
            }
        }

        var inside = Directory.Exists(path) ? processes.FindProcessInside(path) : null;
        if (inside is not null)
        {
            reasons.Add($"{inside} is running from this folder.");
        }

        try
        {
            var inspection = TreeInspector.Inspect(path, cancellationToken);
            stats = inspection.Stats;
            reasons.AddRange(inspection.Problems);
            if (rule is not null && rule.MinimumAgeDays > 0 && stats.Newest > DateTimeOffset.UtcNow.AddDays(-rule.MinimumAgeDays))
            {
                reasons.Add($"Something here changed within the last {rule.MinimumAgeDays} days.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reasons.Add($"The contents can't be fully read: {ex.Message}");
        }

        var decision = OwnershipPolicy.Evaluate(state, 0, protectedReason is not null, rule?.CleanupAllowed == true, isApproved);
        if (!decision.IsAllowed && !reasons.Contains(decision.Reason))
        {
            reasons.Add(decision.Reason);
        }

        return new ValidationOutcome(reasons, stats);
    }

    private static bool Covers(CleanupRule rule, string path) => rule.Scope == "Children"
        ? path.StartsWith(rule.Target + "\\", StringComparison.OrdinalIgnoreCase)
        : ProtectedPathPolicy.IsSameOrInside(path, rule.Target);

    private static bool HasLinkInPath(string path, string root)
    {
        for (var current = path; current is not null && current.Length >= root.Length; current = Path.GetDirectoryName(current))
        {
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record TreeInspection(TreeStats Stats, IReadOnlyList<string> Problems);

/// <summary>Walks an item without following links. It opens files only to find locks.</summary>
public static class TreeInspector
{
    private const int MaxReportedProblems = 5;

    private static readonly EnumerationOptions Options = new() { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };

    public static TreeInspection Inspect(string path, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        long bytes = 0, items = 0;
        var newest = DateTimeOffset.MinValue;
        var pending = new Stack<string>();

        if (File.Exists(path))
        {
            CheckFile(new FileInfo(path), problems, ref bytes, ref items, ref newest);
            return new TreeInspection(new TreeStats(bytes, items, newest), problems);
        }

        pending.Push(path);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = new DirectoryInfo(pending.Pop());
            foreach (var entry in folder.EnumerateFileSystemInfos("*", Options))
            {
                var content = ProtectedPathPolicy.CheckContent(entry.Name);
                if (content is not null)
                {
                    Add(problems, content);
                }

                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Add(problems, $"Contains a link: {entry.FullName}");
                }
                else if (entry is DirectoryInfo)
                {
                    items++;
                    pending.Push(entry.FullName);
                }
                else if (entry is FileInfo file)
                {
                    CheckFile(file, problems, ref bytes, ref items, ref newest);
                }
            }
        }

        return new TreeInspection(new TreeStats(bytes, items, newest), problems);
    }

    private static void CheckFile(FileInfo file, List<string> problems, ref long bytes, ref long items, ref DateTimeOffset newest)
    {
        bytes += file.Length;
        items++;
        var modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        if (modified > newest)
        {
            newest = modified;
        }

        var content = ProtectedPathPolicy.CheckContent(file.Name);
        if (content is not null)
        {
            Add(problems, content);
        }

        if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            Add(problems, $"Contains a link: {file.FullName}");
            return;
        }

        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Add(problems, $"In use or unreadable: {file.FullName}");
        }
    }

    private static void Add(List<string> problems, string problem)
    {
        if (problems.Count < MaxReportedProblems && !problems.Contains(problem))
        {
            problems.Add(problem);
        }
    }
}
