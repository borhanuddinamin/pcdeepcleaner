using System.Security;
using Microsoft.Win32;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Enums;

namespace WCM.Infrastructure.Discovery;

internal sealed record UninstallEntry(
    string Hive,
    string Architecture,
    string SubKey,
    string? DisplayName,
    string? Publisher,
    string? DisplayVersion,
    string? InstallLocation,
    string? DisplayIcon,
    bool IsSystemComponent,
    bool HasParent);

internal static class UninstallEntryMapper
{
    public static ApplicationInfo? Map(UninstallEntry entry, Func<string, bool> directoryExists, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(entry.DisplayName) || entry.IsSystemComponent || entry.HasParent)
        {
            return null;
        }

        var location = string.IsNullOrWhiteSpace(entry.InstallLocation) ? null : entry.InstallLocation.Trim().Trim('"');
        var executable = ParseExecutable(entry.DisplayIcon);
        var missing = (location is not null && !directoryExists(location))
            || (location is null && executable is not null && !fileExists(executable));

        return new ApplicationInfo(
            $"registry:{entry.Hive}:{entry.Architecture}:{entry.SubKey}".ToLowerInvariant(),
            entry.DisplayName.Trim(),
            string.IsNullOrWhiteSpace(entry.Publisher) ? null : entry.Publisher.Trim(),
            string.IsNullOrWhiteSpace(entry.DisplayVersion) ? null : entry.DisplayVersion.Trim(),
            location,
            executable,
            "Registry",
            missing ? ApplicationStatus.InstallPathMissing : ApplicationStatus.Installed,
            entry.Architecture);
    }

    /// <summary>Reads a value such as "C:\App\app.exe",0. Icon files and other types are ignored.</summary>
    public static string? ParseExecutable(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        var value = displayIcon.Trim();
        var comma = value.LastIndexOf(',');
        if (comma > 0 && int.TryParse(value[(comma + 1)..], out _))
        {
            value = value[..comma];
        }

        value = value.Trim().Trim('"');
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(value)
            ? value
            : null;
    }
}

public sealed class RegistryApplicationProvider : IApplicationDiscoveryProvider
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public string ProviderName => "Registry";

    public Task<IReadOnlyList<ApplicationInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var results = new List<ApplicationInfo>();

        // The 32-bit registry view is the WOW6432Node branch.
        foreach (var (hive, hiveName) in new[] { (RegistryHive.LocalMachine, "hklm"), (RegistryHive.CurrentUser, "hkcu") })
        {
            foreach (var (view, architecture) in new[] { (RegistryView.Registry64, "x64"), (RegistryView.Registry32, "x86") })
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadView(hive, hiveName, view, architecture, results);
            }
        }

        return Task.FromResult<IReadOnlyList<ApplicationInfo>>(results);
    }

    private static void ReadView(RegistryHive hive, string hiveName, RegistryView view, string architecture, List<ApplicationInfo> results)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var uninstall = root.OpenSubKey(UninstallPath);
        if (uninstall is null)
        {
            return;
        }

        foreach (var subKeyName in uninstall.GetSubKeyNames())
        {
            try
            {
                using var key = uninstall.OpenSubKey(subKeyName);
                if (key is null)
                {
                    continue;
                }

                var entry = new UninstallEntry(
                    hiveName,
                    architecture,
                    subKeyName,
                    key.GetValue("DisplayName") as string,
                    key.GetValue("Publisher") as string,
                    key.GetValue("DisplayVersion") as string,
                    key.GetValue("InstallLocation") as string,
                    key.GetValue("DisplayIcon") as string,
                    key.GetValue("SystemComponent") is int and not 0,
                    key.GetValue("ParentKeyName") is string);

                var application = UninstallEntryMapper.Map(entry, Directory.Exists, File.Exists);
                if (application is not null)
                {
                    results.Add(application);
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                // One unreadable entry must not stop discovery.
            }
        }
    }
}
