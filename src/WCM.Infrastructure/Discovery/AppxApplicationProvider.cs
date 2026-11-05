using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Xml;
using Microsoft.Win32;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Enums;

namespace WCM.Infrastructure.Discovery;

internal sealed record PackageIdentity(string Name, string Version, string Architecture, string PublisherId)
{
    /// <summary>Parses Name_Version_Architecture_ResourceId_PublisherId. Resource packages return null.</summary>
    public static PackageIdentity? Parse(string fullName)
    {
        var parts = fullName.Split('_');
        return parts.Length == 5 && parts[3].Length == 0 && parts[0].Length > 0
            ? new PackageIdentity(parts[0], parts[1], parts[2], parts[4])
            : null;
    }
}

/// <summary>Reads the per-user package repository in the registry, without PowerShell.</summary>
public sealed partial class AppxApplicationProvider : IApplicationDiscoveryProvider
{
    private const string RepositoryPath =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public string ProviderName => "Appx";

    public Task<IReadOnlyList<ApplicationInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var results = new List<ApplicationInfo>();
        using var repository = Registry.CurrentUser.OpenSubKey(RepositoryPath);
        if (repository is null)
        {
            return Task.FromResult<IReadOnlyList<ApplicationInfo>>(results);
        }

        foreach (var fullName in repository.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = PackageIdentity.Parse(fullName);
            if (identity is null)
            {
                continue;
            }

            try
            {
                using var key = repository.OpenSubKey(fullName);
                var root = key?.GetValue("PackageRootFolder") as string;
                if (root is null || IsFramework(root))
                {
                    continue;
                }

                results.Add(new ApplicationInfo(
                    $"appx:{identity.Name}_{identity.PublisherId}".ToLowerInvariant(),
                    ResolveDisplayName(key!.GetValue("DisplayName") as string, identity.Name),
                    null,
                    identity.Version,
                    root,
                    null,
                    ProviderName,
                    ApplicationStatus.Installed,
                    identity.Architecture));
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                // One unreadable package must not stop discovery.
            }
        }

        return Task.FromResult<IReadOnlyList<ApplicationInfo>>(results);
    }

    private static string ResolveDisplayName(string? displayName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(displayName)
            || displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        if (!displayName.StartsWith('@'))
        {
            return displayName;
        }

        var buffer = new StringBuilder(512);
        var resolved = SHLoadIndirectString(displayName, buffer, buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString() : string.Empty;
        return resolved.Length == 0 || resolved.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? fallback : resolved;
    }

    // Unreadable manifests are kept: listing a package is safer than hiding it.
    private static bool IsFramework(string packageRoot)
    {
        try
        {
            using var reader = XmlReader.Create(
                Path.Combine(packageRoot, "AppxManifest.xml"),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Framework")
                {
                    return reader.ReadElementContentAsString().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return false;
        }

        return false;
    }

#pragma warning disable SYSLIB1054 // StringBuilder output needs the classic marshaller.
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, int outputSize, IntPtr reserved);
#pragma warning restore SYSLIB1054
}
