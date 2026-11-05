namespace WCM.Domain.Policies;

public static class OwnershipState
{
    public const string InstalledProtected = "InstalledProtected";
    public const string PotentialOrphan = "PotentialOrphan";
    public const string ConfirmedOrphanByHistory = "ConfirmedOrphanByHistory";
    public const string SharedProtected = "SharedProtected";
    public const string CacheCandidate = "CacheCandidate";
    public const string UnknownPreserved = "UnknownPreserved";
    public const string UserConfirmedOwnership = "UserConfirmedOwnership";
    public const string UserRejectedMapping = "UserRejectedMapping";
}

public static class EvidenceType
{
    public const string ExactSignature = "ExactSignature";
    public const string ExecutableMetadata = "ExecutableMetadata";
    public const string Publisher = "Publisher";
    public const string KnownPath = "KnownPath";
    public const string Historical = "Historical";
    public const string InstallPath = "InstallPath";
    public const string FolderName = "FolderName";
    public const string Conflict = "Conflict";
    public const string SharedActive = "SharedActive";
    public const string UserDecision = "UserDecision";
}

public sealed record EvidenceItem(string Type, string Description, int Score);

/// <summary>Heuristic evidence scores from the plan, section FR-004. They are not probabilities.</summary>
public static class OwnershipScorer
{
    private static readonly Dictionary<string, int> Weights = new(StringComparer.Ordinal)
    {
        [EvidenceType.ExactSignature] = 30,
        [EvidenceType.ExecutableMetadata] = 25,
        [EvidenceType.Publisher] = 15,
        [EvidenceType.KnownPath] = 15,
        [EvidenceType.Historical] = 10,
        [EvidenceType.InstallPath] = 10,
        [EvidenceType.FolderName] = 5,
        [EvidenceType.Conflict] = -30,
        [EvidenceType.SharedActive] = -40,
        [EvidenceType.UserDecision] = 0,
    };

    public static EvidenceItem Create(string type, string description) => new(type, description, Weights[type]);

    public static int Score(IEnumerable<EvidenceItem> evidence) => Math.Clamp(evidence.Sum(item => item.Score), 0, 100);

    public static string Band(int score) => score switch
    {
        >= 90 => "Strong evidence",
        >= 70 => "High evidence",
        >= 40 => "Possible",
        _ => "Weak/unknown",
    };

    /// <summary>
    /// Evidence that ties a path to one application. A name or publisher match alone is too weak,
    /// but a publisher folder containing a folder named for the application is enough.
    /// </summary>
    public static bool IsStrong(IReadOnlyCollection<EvidenceItem> evidence) =>
        evidence.Any(item => item.Type is EvidenceType.ExactSignature or EvidenceType.KnownPath
            or EvidenceType.ExecutableMetadata or EvidenceType.InstallPath)
        || (evidence.Any(item => item.Type == EvidenceType.Publisher)
            && evidence.Any(item => item.Type == EvidenceType.FolderName));
}

public sealed record CleanupDecision(bool IsAllowed, string Reason);

/// <summary>Safety invariants. The score is taken as input only to show it cannot change the outcome.</summary>
public static class OwnershipPolicy
{
    public static string ExplainProtection(string state) => state switch
    {
        OwnershipState.InstalledProtected => "Belongs to an application that is still detected.",
        OwnershipState.SharedProtected => "Used by more than one application.",
        OwnershipState.UserConfirmedOwnership => "You confirmed that this belongs to the application.",
        OwnershipState.UserRejectedMapping => "You rejected this link, but its real owner is unknown.",
        OwnershipState.UnknownPreserved => "Its owner is unknown, so it is kept.",
        OwnershipState.PotentialOrphan or OwnershipState.ConfirmedOrphanByHistory => "Needs your review before any cleanup.",
        OwnershipState.CacheCandidate => "Needs a cleanup rule and your approval.",
        _ => "The state isn't recognized, so it is kept.",
    };

    public static CleanupDecision Evaluate(string state, int score, bool isProtectedPath, bool hasAllowingRule, bool isUserApproved)
    {
        _ = score;
        if (isProtectedPath)
        {
            return new CleanupDecision(false, "This is a protected path.");
        }

        if (state is not (OwnershipState.PotentialOrphan or OwnershipState.ConfirmedOrphanByHistory or OwnershipState.CacheCandidate))
        {
            return new CleanupDecision(false, ExplainProtection(state));
        }

        if (!hasAllowingRule)
        {
            return new CleanupDecision(false, "No cleanup rule allows this.");
        }

        return isUserApproved
            ? new CleanupDecision(true, "A rule allows this and you approved it.")
            : new CleanupDecision(false, "You haven't approved this.");
    }
}
