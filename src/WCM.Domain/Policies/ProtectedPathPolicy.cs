namespace WCM.Domain.Policies;

/// <summary>Locations and content that cleanup may never touch, whatever a rule or score says.</summary>
public sealed class ProtectedPathPolicy(IReadOnlyList<string> protectedRoots, IReadOnlyList<string> protectedExactFolders, IReadOnlyList<string> userPaths)
{
    private static readonly HashSet<string> ProtectedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".ssh", ".gnupg", ".aws", ".azure", ".kube", "User Data", "Credentials", "Protect", "Vault",
        "volumes", "Docker Desktop", "wsl", "PostgreSQL", "MySQL", "MongoDB", "Microsoft SQL Server",
    };

    private static readonly HashSet<string> SensitiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".db", ".sqlite", ".sqlite3", ".mdf", ".ldf", ".kdbx", ".pfx", ".pem", ".key", ".sln", ".slnx", ".csproj", ".vhdx", ".env",
    };

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "Login Data", "Cookies", "Web Data", "key4.db", "logins.json", "id_rsa", "id_ed25519", ".env", "credentials.json",
    };

    /// <returns>The reason the path is protected, or null.</returns>
    public string? Check(string path)
    {
        foreach (var root in protectedRoots.Concat(userPaths))
        {
            if (IsSameOrInside(path, root))
            {
                return $"Inside a protected location: {root}";
            }
        }

        if (protectedExactFolders.Any(folder => path.Equals(Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase)))
        {
            return "This is a whole tool or profile folder. Only explicit subfolders may be cleaned.";
        }

        var segment = path.Split('\\').FirstOrDefault(ProtectedSegments.Contains);
        return segment is null ? null : $"Contains protected data: {segment}";
    }

    /// <returns>The reason this file or folder name is sensitive, or null.</returns>
    public static string? CheckContent(string name)
    {
        if (SensitiveNames.Contains(name))
        {
            return $"Contains sensitive data: {name}";
        }

        return SensitiveExtensions.Contains(Path.GetExtension(name)) ? $"Contains a database, credential or source file: {name}" : null;
    }

    public static bool IsSameOrInside(string path, string root)
    {
        var normalized = Path.TrimEndingDirectorySeparator(root);
        return path.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalized + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
