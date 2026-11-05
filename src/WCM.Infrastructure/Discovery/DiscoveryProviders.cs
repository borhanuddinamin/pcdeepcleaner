using WCM.Application.Interfaces;

namespace WCM.Infrastructure.Discovery;

public static class DiscoveryProviders
{
    public static IReadOnlyList<IApplicationDiscoveryProvider> CreateDefault() =>
    [
        new RegistryApplicationProvider(),
        new AppxApplicationProvider(),
        PortableApplicationProvider.CreateDefault(),
        StartMenuShortcutProvider.CreateDefault(),
    ];
}
