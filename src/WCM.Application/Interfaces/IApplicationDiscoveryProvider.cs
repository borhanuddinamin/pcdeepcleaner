using WCM.Application.DTOs;

namespace WCM.Application.Interfaces;

public interface IApplicationDiscoveryProvider
{
    string ProviderName { get; }

    Task<IReadOnlyList<ApplicationInfo>> DiscoverAsync(CancellationToken cancellationToken);
}

public interface IApplicationDiscoveryService
{
    Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken);
}
