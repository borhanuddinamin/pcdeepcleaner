using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WCM.Domain.Policies;

namespace WCM.CleanupEngine.Quarantine;

/// <summary>
/// The only production code allowed to move or remove files. A safety test enforces this.
/// Nothing here overwrites, and removal needs either a path inside quarantine or a verified copy.
/// </summary>
internal static partial class FileOperations
{
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Moves on the same volume. It fails instead of overwriting.</summary>
    public static void Move(string source, string destination)
    {
        if (Exists(destination))
        {
            throw new IOException($"The destination already exists: {destination}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(source))
        {
            File.Move(source, destination, overwrite: false);
        }
        else
        {
            Directory.Move(source, destination);
        }
    }

    /// <summary>Copies to a path that must not exist yet, keeping timestamps and attributes.</summary>
    public static void CopyTree(string source, string destination, CancellationToken cancellationToken)
    {
        if (Exists(destination))
        {
            throw new IOException($"The destination already exists: {destination}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: false);
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var folder in Directory.EnumerateDirectories(source))
        {
            CopyTree(folder, Path.Combine(destination, Path.GetFileName(folder)), cancellationToken);
        }

        Directory.SetLastWriteTimeUtc(destination, Directory.GetLastWriteTimeUtc(source));
    }

    /// <summary>Hashes relative names, sizes and content, so it detects a changed or missing file.</summary>
    public static string HashTree(string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (File.Exists(path))
        {
            AddFile(hash, path, string.Empty);
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        var files = Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false })
            .Select(entry => Path.GetRelativePath(path, entry))
            .Order(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = Path.Combine(path, relative);
            if (File.Exists(full))
            {
                AddFile(hash, full, relative);
            }
            else
            {
                hash.AppendData(Encoding.UTF8.GetBytes(relative.ToUpperInvariant() + "\\"));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <returns>The reason a cross-volume copy would lose data, or null.</returns>
    public static string? FindCopyProblem(string path)
    {
        IEnumerable<string> files = File.Exists(path)
            ? [path]
            : Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false });
        foreach (var file in files)
        {
            if (HasExtraStreams(file))
            {
                return $"Has alternate data streams that copying can lose: {file}";
            }

            var security = new FileInfo(file).GetAccessControl();
            if (security.AreAccessRulesProtected || security.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)).Count > 0)
            {
                return $"Has its own permissions that copying can lose: {file}";
            }
        }

        return null;
    }

    /// <summary>Removes an original only after the caller has verified a matching copy.</summary>
    public static void RemoveOriginalAfterVerifiedCopy(string path, string verifiedHash, CancellationToken cancellationToken)
    {
        if (!HashTree(path, cancellationToken).Equals(verifiedHash, StringComparison.Ordinal))
        {
            throw new IOException($"The original changed during the copy, so it was kept: {path}");
        }

        Remove(path);
    }

    /// <summary>Permanently removes a path that must be strictly inside quarantine and contain no links.</summary>
    public static void DeleteInsideQuarantine(string path, string quarantineRoot)
    {
        var full = Path.GetFullPath(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(quarantineRoot));
        if (!full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refused: this path isn't inside quarantine: {path}");
        }

        for (var current = full; current is not null && current.Length > root.Length; current = Path.GetDirectoryName(current))
        {
            if (Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException($"Refused: the path goes through a link: {current}");
            }
        }

        if (Directory.Exists(full) && Directory
            .EnumerateFileSystemEntries(full, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false })
            .Any(entry => File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint)))
        {
            throw new InvalidOperationException($"Refused: quarantined content contains a link: {path}");
        }

        Remove(full);
    }

    private static void Remove(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        else if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void AddFile(IncrementalHash hash, string path, string relative)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(relative.ToUpperInvariant() + "|"));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
    }

    private static bool HasExtraStreams(string path)
    {
        var handle = FindFirstStreamW(path, 0, out var data, 0);
        if (handle == new IntPtr(-1))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 38)
            {
                return false;
            }

            throw new IOException($"Couldn't read the streams of {path}", new Win32Exception(error));
        }

        try
        {
            var count = 0;
            do
            {
                count++;
            }
            while (FindNextStreamW(handle, out data));
            return count > 1;
        }
        finally
        {
            FindClose(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StreamData
    {
        public long Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
        public string Name;
    }

#pragma warning disable SYSLIB1054, CA1838
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstStreamW(string fileName, int infoLevel, out StreamData data, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStreamW(IntPtr handle, out StreamData data);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr handle);
#pragma warning restore SYSLIB1054, CA1838
}
