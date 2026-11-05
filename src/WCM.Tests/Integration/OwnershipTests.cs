using System.Diagnostics;
using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Application.Services;
using WCM.Domain.Enums;
using WCM.Domain.Policies;
using WCM.Infrastructure.Discovery;
using WCM.Infrastructure.FileSystem;
using WCM.Infrastructure.SQLite;
using WCM.Tests.Fixtures;
using Xunit.Abstractions;

namespace WCM.Tests.Integration;

public sealed class OwnershipTests : IDisposable
{
    private readonly SyntheticDataFixture _fixture = new();
    private readonly SyntheticDataFixture _tree = new();
    private readonly SqliteConnectionFactory _connections;
    private readonly ApplicationRepository _applications;
    private readonly MappingRepository _mappings;
    private readonly ITestOutputHelper _output;

    public OwnershipTests(ITestOutputHelper output)
    {
        _output = output;
        _connections = new SqliteConnectionFactory(Path.Combine(_fixture.Root, "cleanup.db"));
        new DatabaseInitializer(_connections).Initialize();
        _applications = new ApplicationRepository(_connections);
        _mappings = new MappingRepository(_connections);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        _tree.Dispose();
    }

    [Fact]
    public async Task Mapping_needs_applications_and_a_finished_scan()
    {
        var service = Service(Config());
        Assert.Equal("Scan for applications first.", (await service.MapAsync(CancellationToken.None)).Problem);

        await AddApp("a", "Foo");
        Assert.Equal("Run a storage scan first.", (await service.MapAsync(CancellationToken.None)).Problem);
    }

    [Fact]
    public async Task Mapping_saves_evidence_and_everything_stays_protected()
    {
        var install = Path.Combine(_tree.Root, "Foo");
        _tree.AddFile(@"Foo\foo.dat", "12345");
        _tree.AddFile(@"Unrelated\x.dat");
        _tree.AddFile(@"Foo\deep\deeper\deepest\y.dat");
        await AddApp("a", "Foo", install);
        await new StorageScanner(new ScanRepository(_connections)).ScanAsync(Config(), null, CancellationToken.None);

        var result = await Service(Config()).MapAsync(CancellationToken.None);

        Assert.True(result.WasSaved);
        Assert.Equal(1, result.UnknownLocations);
        var locations = await _mappings.GetLocationsAsync("a", CancellationToken.None);
        var foo = locations.Single(location => location.Path == install);
        Assert.True(foo.IsProtected);
        Assert.Equal(OwnershipState.InstalledProtected, foo.State);
        Assert.Contains(foo.Evidence, item => item.Type == EvidenceType.InstallPath);
        Assert.False(string.IsNullOrEmpty(foo.ProtectionReason));

        using var connection = _connections.Create();
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ApplicationStorageMappings WHERE IsProtected <> 1"));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM StorageLocations WHERE Path LIKE '%deepest%'"));
        Assert.Equal("UnknownPreserved", connection.ExecuteScalar<string>("SELECT OwnershipStatus FROM StorageLocations WHERE Path LIKE '%Unrelated'"));
    }

    [Fact]
    public async Task Install_folder_outside_scanned_roots_is_recorded_as_not_measured()
    {
        _tree.AddFile("a.dat");
        await AddApp("a", "Foo", @"Z:\Elsewhere\Foo");
        await new StorageScanner(new ScanRepository(_connections)).ScanAsync(Config(), null, CancellationToken.None);

        await Service(Config()).MapAsync(CancellationToken.None);

        var location = Assert.Single(await _mappings.GetLocationsAsync("a", CancellationToken.None));
        Assert.Equal(@"Z:\Elsewhere\Foo", location.Path);
        Assert.Equal(0, location.SizeBytes);
    }

    [Fact]
    public async Task Decision_survives_remapping_is_audited_and_stays_protected()
    {
        var install = Path.Combine(_tree.Root, "Foo");
        _tree.AddFile(@"Foo\foo.dat");
        await AddApp("a", "Foo", install);
        await new StorageScanner(new ScanRepository(_connections)).ScanAsync(Config(), null, CancellationToken.None);
        var service = Service(Config());
        await service.MapAsync(CancellationToken.None);
        var mappingId = (await _mappings.GetLocationsAsync("a", CancellationToken.None)).Single(l => l.Path == install).MappingId;

        await service.SetDecisionAsync(mappingId, UserDecision.Rejected, CancellationToken.None);
        await service.MapAsync(CancellationToken.None);

        var location = (await _mappings.GetLocationsAsync("a", CancellationToken.None)).Single(l => l.Path == install);
        Assert.Equal(OwnershipState.UserRejectedMapping, location.State);
        Assert.True(location.IsProtected);
        Assert.Contains(location.Evidence, item => item.Type == EvidenceType.UserDecision);
        Assert.Contains(await new AuditRepository(_connections).GetRecentAsync(5, CancellationToken.None), entry => entry.Action == "MappingDecision");
    }

    [Fact]
    public async Task Unknown_decision_is_refused() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Service(Config()).SetDecisionAsync(1, "Delete", CancellationToken.None));

    [Fact]
    public void Shipped_signatures_load_and_never_name_a_top_level_tool_folder_as_cache()
    {
        var catalog = new SignatureCatalog(Path.Combine(RepositoryRoot.Path, "Rules", "application-signatures.json"));

        var signatures = catalog.Load();

        Assert.NotEmpty(signatures);
        Assert.All(signatures, signature => Assert.All(signature.Paths, path => Assert.True(Path.IsPathFullyQualified(path))));
    }

    [Theory]
    [InlineData("""{ "catalogueVersion": "1", "signatures": [ { "signatureId": "a", "signatureVersion": "1", "nameContains": "A", "paths": ["%LOCALAPPDATA%"] } ] }""")]
    [InlineData("""{ "catalogueVersion": "1", "signatures": [ { "signatureId": "a", "signatureVersion": "1", "nameContains": "A", "paths": ["C:\\Windows"] } ] }""")]
    [InlineData("""{ "catalogueVersion": "1", "signatures": [ { "signatureId": "a", "signatureVersion": "1", "nameContains": "A", "paths": [] } ] }""")]
    [InlineData("""{ "catalogueVersion": "1", "signatures": [ { "signatureId": "a", "signatureVersion": "1", "nameContains": "A", "paths": ["%WCM_NOT_SET%\\x"] } ] }""")]
    [InlineData("""{ "catalogueVersion": "1", "signatures": [ { "unknownField": true } ] }""")]
    [InlineData("{ not json")]
    public void Invalid_signature_files_fail_closed(string content)
    {
        var path = _fixture.AddFile("signatures.json", content);

        Assert.Throws<InvalidDataException>(() => new SignatureCatalog(path).Load());
    }

    [Fact]
    public void Missing_signature_file_fails_closed() =>
        Assert.Throws<InvalidDataException>(() => new SignatureCatalog(Path.Combine(_fixture.Root, "none.json")).Load());

    // Reads this PC's real folders into a temporary database. Run with WCM_REAL_SCAN=1.
    [Fact]
    public async Task Real_mapping_of_this_machine()
    {
        if (Environment.GetEnvironmentVariable("WCM_REAL_SCAN") != "1")
        {
            return;
        }

        var audit = new AuditRepository(_connections);
        await new ApplicationDiscoveryService(DiscoveryProviders.CreateDefault(), _applications, audit).DiscoverAsync(CancellationToken.None);
        var configuration = ScanDefaults.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsCleanupManager"));
        await new StorageScanner(new ScanRepository(_connections)).ScanAsync(configuration, null, CancellationToken.None);
        var clock = Stopwatch.StartNew();
        var service = new OwnershipService(
            _applications,
            _mappings,
            new CandidateLocationReader(_connections),
            new FixedSettings(configuration),
            new SignatureCatalog(Path.Combine(RepositoryRoot.Path, "Rules", "application-signatures.json")),
            audit);

        var result = await service.MapAsync(CancellationToken.None);

        _output.WriteLine($"REAL saved={result.WasSaved} locations={result.Locations} mappings={result.Mappings} shared={result.SharedLocations} unknown={result.UnknownLocations} seconds={clock.Elapsed.TotalSeconds:N1} problem={result.Problem}");
        using var connection = _connections.Create();
        foreach (var row in connection.Query<(string State, long Count)>("SELECT OwnershipStatus, COUNT(*) FROM ApplicationStorageMappings GROUP BY 1"))
        {
            _output.WriteLine($"REAL state {row.State}={row.Count}");
        }

        foreach (var row in connection.Query<(string Type, long Count)>("SELECT EvidenceType, COUNT(*) FROM OwnershipEvidence GROUP BY 1"))
        {
            _output.WriteLine($"REAL evidence {row.Type}={row.Count}");
        }

        _output.WriteLine($"REAL appsWithLocation={connection.ExecuteScalar<long>("SELECT COUNT(DISTINCT ApplicationId) FROM ApplicationStorageMappings")} of {connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Applications")}");
        _output.WriteLine($"REAL notProtected={connection.ExecuteScalar<long>("SELECT COUNT(*) FROM ApplicationStorageMappings WHERE IsProtected <> 1")}");
        foreach (var row in connection.Query<(string Path, long Owners)>(
            "SELECT s.Path, COUNT(*) FROM ApplicationStorageMappings m JOIN StorageLocations s ON s.Id = m.StorageLocationId WHERE m.OwnershipStatus = 'SharedProtected' GROUP BY 1 ORDER BY 2 DESC LIMIT 12"))
        {
            _output.WriteLine($"REAL shared {row.Path} owners={row.Owners}");
        }

        foreach (var row in connection.Query<(string Name, string Path, long Score, string State)>(
            "SELECT a.Name, s.Path, m.ConfidenceScore, m.OwnershipStatus FROM ApplicationStorageMappings m JOIN Applications a ON a.Id = m.ApplicationId JOIN StorageLocations s ON s.Id = m.StorageLocationId WHERE a.Name LIKE '%Chrome%' OR a.Name LIKE '%Cursor%' OR a.Name LIKE '7-Zip%' OR a.Name LIKE '%Visual Studio Code%' ORDER BY 1, 3 DESC LIMIT 25"))
        {
            _output.WriteLine($"REAL sample {row.Name} | {row.Path} | {row.Score} | {row.State}");
        }

        Assert.True(result.WasSaved);
        Assert.Equal(0, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM ApplicationStorageMappings WHERE IsProtected <> 1"));
    }

    private ScanConfiguration Config() => new([new ScanRoot(_tree.Root, "Local application data", "t")], [], 2);

    private OwnershipService Service(ScanConfiguration configuration) => new(
        _applications,
        _mappings,
        new CandidateLocationReader(_connections),
        new FixedSettings(configuration),
        new FixedSignatures(),
        new AuditRepository(_connections));

    private Task AddApp(string key, string name, string? install = null) => _applications.UpsertAsync(
        new ApplicationRecord(key, name, null, null, install, null, "Registry", ApplicationStatus.Installed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        CancellationToken.None);

    private sealed class FixedSettings(ScanConfiguration configuration) : IScanConfigurationStore
    {
        public Task<ScanConfiguration> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(configuration);

        public Task SaveAsync(ScanConfiguration value, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedSignatures : ISignatureCatalog
    {
        public IReadOnlyList<KnownApplicationSignature> Load() => [];
    }
}
