using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Domain.Enums;

namespace WCM.Infrastructure.Discovery;

/// <summary>
/// Looks one level inside known portable roots. A program counts only when its executable has
/// product metadata. Links are not followed.
/// </summary>
public sealed class PortableApplicationProvider(IReadOnlyList<string> roots) : IApplicationDiscoveryProvider
{
    private static readonly string[] IgnoredExecutableParts = ["unins", "setup", "update", "install", "crash", "helper"];

    public string ProviderName => "Portable";

    public static PortableApplicationProvider CreateDefault()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new PortableApplicationProvider(
            [Path.Combine(local, "Programs"), Path.Combine(profile, "PortableApps")]);
    }

    public Task<IReadOnlyList<ApplicationInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var results = new List<ApplicationInfo>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var folder in Directory.EnumerateDirectories(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var application = Map(folder, options);
                if (application is not null)
                {
                    results.Add(application);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ApplicationInfo>>(results);
    }

    private ApplicationInfo? Map(string folder, EnumerationOptions options)
    {
        var candidates = Directory.EnumerateFiles(folder, "*.exe", options)
            .Where(file => !IgnoredExecutableParts.Any(part =>
                Path.GetFileName(file).Contains(part, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase);

        foreach (var executable in candidates)
        {
            var metadata = ApplicationMetadataReader.Read(executable);
            if (metadata?.ProductName is not null)
            {
                return new ApplicationInfo(
                    $"portable:{executable}".ToLowerInvariant(),
                    metadata.ProductName,
                    metadata.Company,
                    metadata.Version,
                    folder,
                    executable,
                    ProviderName,
                    ApplicationStatus.Portable);
            }
        }

        return null;
    }
}
