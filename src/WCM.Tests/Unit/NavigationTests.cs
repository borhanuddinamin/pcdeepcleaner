using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Desktop.ViewModels;

namespace WCM.Tests.Unit;

public class NavigationTests
{
    [Fact]
    public void Shell_lists_the_nine_specified_screens_in_order()
    {
        var titles = Shell(new FakeDirectories()).Pages.Select(p => p.Title);

        Assert.Equal(
            ["Dashboard", "Applications", "Orphaned Data", "Cache Analyzer", "Storage Explorer",
             "Cleanup Center", "Quarantine & Restore", "Scan History", "Settings"],
            titles);
    }

    [Theory]
    [InlineData(true, "Settings")]
    [InlineData(false, "Dashboard")]
    public void Opens_settings_only_on_first_run(bool firstRun, string expected) =>
        Assert.Equal(expected, Shell(new FakeDirectories { IsFirstRun = firstRun }).SelectedPage.Title);

    [Fact]
    public void Save_errors_are_shown_to_the_user()
    {
        var settings = new SettingsViewModel(new FakeDirectories { SaveError = new ArgumentException("Bad path") });

        settings.SaveCommand.Execute(null);

        Assert.Equal("Bad path", settings.Status);
    }

    [Fact]
    public async Task Failed_scan_is_reported_and_does_not_claim_results()
    {
        var page = new ApplicationsViewModel(
            new FakeDiscovery(new DiscoveryResult(0, 0, false, false, ["Registry: denied"])),
            new FakeApplications(), new FakeOwnership());

        await page.ScanCommand.ExecuteAsync(null);

        Assert.Contains("Scan failed", page.Status, StringComparison.Ordinal);
        Assert.Contains("Registry: denied", page.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Partial_scan_says_it_is_incomplete()
    {
        var page = new ApplicationsViewModel(
            new FakeDiscovery(new DiscoveryResult(3, 3, false, true, ["Appx: denied"])),
            new FakeApplications(), new FakeOwnership());

        await page.ScanCommand.ExecuteAsync(null);

        Assert.Contains("incomplete", page.Status, StringComparison.Ordinal);
    }

    private static MainWindowViewModel Shell(FakeDirectories directories) => new(
        new SettingsViewModel(directories),
        new ApplicationsViewModel(new FakeDiscovery(new DiscoveryResult(0, 0, true, true, [])), new FakeApplications(), new FakeOwnership()),
        new StorageViewModel(new FakeScanner(), new FakeScanSettings()));

    private sealed class FakeOwnership : IOwnershipService
    {
        public Task<MappingResult> MapAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MappingResult(false, 0, 0, 0, 0, "Run a storage scan first."));

        public Task<IReadOnlyList<LocationMapping>> GetLocationsAsync(string appKey, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LocationMapping>>([]);

        public Task SetDecisionAsync(long mappingId, string decision, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeScanner : IStorageScanner
    {
        public Task<ScanResult> ScanAsync(ScanConfiguration configuration, IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new ScanResult(1, ScanStatus.Completed, 0, 0, 0, 0, 0, 0, 0, null));
    }

    private sealed class FakeScanSettings : IScanConfigurationStore
    {
        public Task<ScanConfiguration> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ScanConfiguration([], [], 1));

        public Task SaveAsync(ScanConfiguration configuration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeDiscovery(DiscoveryResult result) : IApplicationDiscoveryService
    {
        public Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class FakeApplications : IApplicationRepository
    {
        public Task UpsertAsync(ApplicationRecord application, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ApplicationRecord?> GetByKeyAsync(string appKey, CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationRecord?>(null);

        public Task<IReadOnlyList<ApplicationRecord>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApplicationRecord>>([]);

        public Task SaveInventoryAsync(
            IReadOnlyList<MergedApplication> applications,
            DateTimeOffset observedAt,
            bool isComplete,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeDirectories : IDataDirectoryService
    {
        public Exception? SaveError { get; init; }

        public bool IsPortable => false;

        public bool IsFirstRun { get; init; }

        public string DataRoot => @"C:\Data";

        public string LogsDirectory => @"C:\Data\Logs";

        public string DatabasePath => @"C:\Data\Data\cleanup.db";

        public void SaveDataRoot(string path)
        {
            if (SaveError is not null)
            {
                throw SaveError;
            }
        }
    }
}
