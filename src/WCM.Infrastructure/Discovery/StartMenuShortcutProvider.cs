using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Enums;

namespace WCM.Infrastructure.Discovery;

public sealed class StartMenuShortcutProvider(IReadOnlyList<string> roots, string windowsDirectory) : IApplicationDiscoveryProvider
{
    private static readonly string[] IgnoredNameParts = ["uninstall", "readme", "read me", "help", "documentation", "website", "license"];

    public string ProviderName => "StartMenu";

    public static StartMenuShortcutProvider CreateDefault() => new(
        [
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        ],
        Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    public Task<IReadOnlyList<ApplicationInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var results = new List<ApplicationInfo>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var shortcut in Directory.EnumerateFiles(root, "*.lnk", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var application = Map(shortcut);
                if (application is not null)
                {
                    results.Add(application);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ApplicationInfo>>(results);
    }

    private ApplicationInfo? Map(string shortcut)
    {
        var name = Path.GetFileNameWithoutExtension(shortcut);
        if (IgnoredNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var target = ShortcutReader.ReadTarget(shortcut);
        if (target is null
            || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(target)
            || target.StartsWith(windowsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var metadata = ApplicationMetadataReader.Read(target);
        return new ApplicationInfo(
            $"shortcut:{target}".ToLowerInvariant(),
            name,
            metadata?.Company,
            metadata?.Version,
            Path.GetDirectoryName(target),
            target,
            ProviderName,
            ApplicationStatus.Unverified);
    }
}

/// <summary>Reads a shortcut target with no repair or link-tracking side effects.</summary>
internal static class ShortcutReader
{
    public static string? ReadTarget(string shortcutPath)
    {
        object? link = null;
        try
        {
            link = new ShellLinkCoClass();
            ((IPersistFile)link).Load(shortcutPath, 0);
            var buffer = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            return buffer.Length == 0 ? null : buffer.ToString();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (link is not null)
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }

    // Only the first method of the interface is declared, which keeps the vtable valid.
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
    }
}
