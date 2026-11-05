using System.Diagnostics;
using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Application.Services;
using WCM.Domain.Enums;
using WCM.Infrastructure.Discovery;
using WCM.Infrastructure.SQLite;
using WCM.Tests.Fixtures;
using Xunit.Abstractions;

namespace WCM.Tests.Integration;

public sealed class DiscoveryTests : IDisposable
{
    private readonly SyntheticDataFixture _fixture = new();
    private readonly SqliteConnectionFactory _connections;
    private readonly ApplicationRepository _applications;
    private readonly AuditRepository _audit;
    private readonly ITestOutputHelper _output;

    public DiscoveryTests(ITestOutputHelper output)
    {
        _output = output;
        _connections = new SqliteConnectionFactory(Path.Combine(_fixture.Root, "Data", "cleanup.db"));
        new DatabaseInitializer(_connections).Initialize();
        _applications = new ApplicationRepository(_connections);
        _audit = new AuditRepository(_connections);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Complete_discovery_saves_sources_and_snapshots_then_marks_absent_apps()
    {
        await Service(new FakeProvider(App("a"), App("b"))).DiscoverAsync(CancellationToken.None);
        var result = await Service(new FakeProvider(App("a"))).DiscoverAsync(CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal(ApplicationStatus.Installed, (await _applications.GetByKeyAsync("a", CancellationToken.None))!.Status);
        Assert.Equal(ApplicationStatus.NotDetected, (await _applications.GetByKeyAsync("b", CancellationToken.None))!.Status);

        using var connection = _connections.Create();
        Assert.Equal(4, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ApplicationSnapshots"));
        Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ApplicationSourceRecords"));
    }

    [Fact]
    public async Task Failed_provider_never_marks_applications_absent()
    {
        await Service(new FakeProvider(App("a"), App("b"))).DiscoverAsync(CancellationToken.None);

        var result = await Service(new FakeProvider(App("a")), new FailingProvider()).DiscoverAsync(CancellationToken.None);

        Assert.False(result.IsComplete);
        Assert.Contains("Failing: denied", result.Failures);
        Assert.Equal(ApplicationStatus.Installed, (await _applications.GetByKeyAsync("b", CancellationToken.None))!.Status);
        Assert.Equal("Partial", (await _audit.GetRecentAsync(1, CancellationToken.None))[0].Result);
    }

    [Fact]
    public async Task Empty_discovery_changes_nothing_and_is_reported_as_failed()
    {
        await Service(new FakeProvider(App("a"))).DiscoverAsync(CancellationToken.None);

        var result = await Service(new FakeProvider()).DiscoverAsync(CancellationToken.None);

        Assert.False(result.WasSaved);
        Assert.Equal(ApplicationStatus.Installed, (await _applications.GetByKeyAsync("a", CancellationToken.None))!.Status);
        Assert.Equal("Failed", (await _audit.GetRecentAsync(1, CancellationToken.None))[0].Result);
    }

    [Fact]
    public async Task Cancellation_saves_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service(new FakeProvider(App("a"))).DiscoverAsync(cancelled.Token));

        Assert.Empty(await _applications.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Portable_provider_needs_metadata_and_does_not_follow_junctions()
    {
        var root = Path.Combine(_fixture.Root, "Portable");
        var outside = Path.Combine(_fixture.Root, "Outside");
        Directory.CreateDirectory(Path.Combine(root, "Real"));
        Directory.CreateDirectory(outside);
        File.Copy(Environment.ProcessPath!, Path.Combine(root, "Real", "tool.exe"));
        File.Copy(Environment.ProcessPath!, Path.Combine(outside, "linked.exe"));
        _fixture.AddFile(@"Portable\NoMetadata\fake.exe");
        using (var junction = Process.Start(new ProcessStartInfo("cmd.exe", $"""/c mklink /J "{Path.Combine(root, "Link")}" "{outside}" """) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
        {
            await junction.WaitForExitAsync();
            Assert.Equal(0, junction.ExitCode);
        }

        var results = await new PortableApplicationProvider([root]).DiscoverAsync(CancellationToken.None);

        // rmdir removes only the link. Directory.Delete is denied on junctions without elevation.
        using (var unlink = Process.Start(new ProcessStartInfo("cmd.exe", $"""/c rmdir "{Path.Combine(root, "Link")}" """) { UseShellExecute = false, CreateNoWindow = true })!)
        {
            await unlink.WaitForExitAsync();
            Assert.Equal(0, unlink.ExitCode);
        }

        var app = Assert.Single(results);
        Assert.Equal(Path.Combine(root, "Real"), app.InstallLocation);
        Assert.Equal(ApplicationStatus.Portable, app.Status);
        Assert.True(File.Exists(Path.Combine(outside, "linked.exe")));
    }

    [Fact]
    public async Task Discovery_on_this_machine_finds_applications_with_controlled_duplicates()
    {
        var service = new ApplicationDiscoveryService(DiscoveryProviders.CreateDefault(), _applications, _audit);

        var result = await service.DiscoverAsync(CancellationToken.None);
        var stored = await _applications.GetAllAsync(CancellationToken.None);

        _output.WriteLine($"sources={result.SourceRecordCount} apps={result.ApplicationCount} complete={result.IsComplete}");
        foreach (var failure in result.Failures)
        {
            _output.WriteLine($"FAILURE {failure}");
        }

        foreach (var group in stored.GroupBy(app => app.Status).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"{group.Key}={group.Count()}");
        }

        foreach (var group in stored.GroupBy(app => app.DetectionSource).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"[{group.Key}]={group.Count()} e.g. {group.First().Name}");
        }

        foreach (var duplicate in stored.GroupBy(app => app.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            _output.WriteLine($"DUPLICATE {duplicate.Key} x{duplicate.Count()}");
        }

        Assert.True(result.WasSaved);
        Assert.NotEmpty(stored);
        Assert.DoesNotContain(stored, app => app.Name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.ApplicationCount <= result.SourceRecordCount);
    }

    private ApplicationDiscoveryService Service(params IApplicationDiscoveryProvider[] providers) =>
        new(providers, _applications, _audit);

    private static ApplicationInfo App(string key) =>
        new(key, "App " + key, null, null, null, null, "Registry", ApplicationStatus.Installed);

    private sealed class FakeProvider(params ApplicationInfo[] applications) : IApplicationDiscoveryProvider
    {
        public string ProviderName => "Fake";

        public Task<IReadOnlyList<ApplicationInfo>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApplicationInfo>>(applications);
    }

    private sealed class FailingProvider : IApplicationDiscoveryProvider
    {
        public string ProviderName => "Failing";

        public Task<IReadOnlyList<ApplicationInfo>> DiscoverAsync(CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("denied");
    }
}
