using WCM.Application.DTOs;

namespace WCM.Application.Interfaces;

public interface IStorageScanner
{
    /// <summary>Reads metadata only. Cancellation returns a result with status Cancelled.</summary>
    Task<ScanResult> ScanAsync(
        ScanConfiguration configuration,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IScanConfigurationStore
{
    Task<ScanConfiguration> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ScanConfiguration configuration, CancellationToken cancellationToken);
}
