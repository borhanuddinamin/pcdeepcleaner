namespace WCM.Domain.Enums;

public static class ApplicationStatus
{
    public const string Installed = "Installed";
    public const string Portable = "Portable";

    /// <summary>Only a shortcut points to this program, so nothing confirms an installation.</summary>
    public const string Unverified = "Unverified";

    public const string InstallPathMissing = "InstallPathMissing";
    public const string NeedsReview = "NeedsReview";

    /// <summary>Absent from a complete discovery. This does not prove it was uninstalled.</summary>
    public const string NotDetected = "NotDetected";
}
