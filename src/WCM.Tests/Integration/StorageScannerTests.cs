using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Infrastructure.FileSystem;
using WCM.Infrastructure.SQLite;
using WCM.Tests.Fixtures;
using Xunit.Abstractions;

namespace WCM.Tests.Integration;

public sealed class StorageScannerTests : IDisposable
{
    private readonly SyntheticDataFixture _fixture = new();
    private readonly SyntheticDataFixture _database = new();
    private readonly SqliteConnectionFactory _connections;
    private readonly ScanRepository _repository;
    private readonly StorageScanner _scanner;
    private readonly ITestOutputHelper _output;

    public StorageScannerTests(ITestOutputHelper output)
    {
        _output = output;
        _connections = new SqliteConnectionFactory(Path.Combine(_database.Root, "cleanup.db"));
        new DatabaseInitializer(_connections).Initialize();
        _repository = new ScanRepository(_connections);
        _scanner = new StorageScanner(_repository);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        _database.Dispose();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task Totals_and_folder_sizes_are_correct_at_any_concurrency(int concurrency)
    {
        BuildTree();

        var result = await _scanner.ScanAsync(Config(concurrency), null, CancellationToken.None);

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.True(result.IsComplete);
        Assert.Equal(200, result.Files);
        Assert.Equal(1000, result.Bytes);
        Assert.Equal(11, result.Folders);

        using var connection = _connections.Create();
        Assert.Equal(1000, connection.ExecuteScalar<long>("SELECT SizeBytes FROM ScanItems WHERE Path = @Root", new { Root = _fixture.Root }));
        Assert.Equal(100, connection.ExecuteScalar<long>("SELECT SizeBytes FROM ScanItems WHERE Path = @P", new { P = Path.Combine(_fixture.Root, "dir3") }));
        Assert.Equal(211, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM ScanItems"));
        Assert.Equal(1000, (await _repository.GetSessionAsync(result.SessionId, CancellationToken.None))!.TotalBytes);
    }

    [Fact]
    public async Task Hidden_and_system_files_are_counted()
    {
        var path = _fixture.AddFile("hidden.bin", "12345");
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);

        var result = await _scanner.ScanAsync(Config(), null, CancellationToken.None);

        Assert.Equal(1, result.Files);
        Assert.Equal(5, result.Bytes);
        File.SetAttributes(path, FileAttributes.Normal);
    }

    [Fact]
    public async Task Junction_is_recorded_and_its_target_is_not_read()
    {
        _fixture.AddFile("inside.txt", "12");
        _database.AddFile(@"outside\secret.txt", "1234567890");
        var link = Path.Combine(_fixture.Root, "Link");
        await RunCmd($"""/c mklink /J "{link}" "{Path.Combine(_database.Root, "outside")}" """);

        try
        {
            var result = await _scanner.ScanAsync(Config(), null, CancellationToken.None);

            Assert.Equal(1, result.LinksSkipped);
            Assert.Equal(1, result.Files);
            Assert.Equal(2, result.Bytes);
            using var connection = _connections.Create();
            Assert.Equal("Link", connection.ExecuteScalar<string>("SELECT ItemType FROM ScanItems WHERE Path = @link", new { link }));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ScanItems WHERE Path LIKE '%secret.txt'"));
        }
        finally
        {
            await RunCmd($"""/c rmdir "{link}" """);
        }
    }

    [Fact]
    public async Task Root_that_is_a_junction_is_not_entered()
    {
        _database.AddFile(@"outside\secret.txt", "1234567890");
        var link = Path.Combine(_fixture.Root, "RootLink");
        await RunCmd($"""/c mklink /J "{link}" "{Path.Combine(_database.Root, "outside")}" """);

        try
        {
            var result = await _scanner.ScanAsync(new ScanConfiguration([new ScanRoot(link, "t", "t")], [], 2), null, CancellationToken.None);

            Assert.Equal(1, result.LinksSkipped);
            Assert.Equal(0, result.Files);
        }
        finally
        {
            await RunCmd($"""/c rmdir "{link}" """);
        }
    }

    [Fact]
    public async Task Access_denied_is_recorded_and_the_scan_continues_but_is_not_complete()
    {
        _fixture.AddFile("ok.txt", "123");
        _fixture.AddFile(@"denied\hidden.txt", "12345");
        var denied = new DirectoryInfo(Path.Combine(_fixture.Root, "denied"));
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = denied.GetAccessControl();
        security.AddAccessRule(rule);
        denied.SetAccessControl(security);

        try
        {
            var result = await _scanner.ScanAsync(Config(), null, CancellationToken.None);

            Assert.Equal(ScanStatus.CompletedWithErrors, result.Status);
            Assert.False(result.IsComplete);
            Assert.Equal(1, result.Errors);
            Assert.Equal(1, result.Files);
            using var connection = _connections.Create();
            Assert.Equal("AccessDenied", connection.ExecuteScalar<string>("SELECT Kind FROM ScanErrors"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT IsPartial FROM ScanItems WHERE Path = @P", new { P = denied.FullName }));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT IsPartial FROM ScanItems WHERE Path = @P", new { P = _fixture.Root }));
        }
        finally
        {
            security.RemoveAccessRule(rule);
            denied.SetAccessControl(security);
        }
    }

    [Fact]
    public async Task Missing_root_does_not_stop_the_scan()
    {
        _fixture.AddFile("a.txt");
        var config = new ScanConfiguration(
            [new ScanRoot(Path.Combine(_fixture.Root, "gone"), "t", "t"), new ScanRoot(_fixture.Root, "t", "t")], [], 2);

        var result = await _scanner.ScanAsync(config, null, CancellationToken.None);

        Assert.Equal(1, result.MissingRoots);
        Assert.Equal(1, result.Files);
    }

    [Fact]
    public async Task Excluded_folder_is_not_read_and_is_recorded()
    {
        _fixture.AddFile(@"keep\a.txt", "1");
        _fixture.AddFile(@"skip\b.txt", "1");
        var config = new ScanConfiguration(
            [new ScanRoot(_fixture.Root, "t", "t")],
            [new ScanExclusion(Path.Combine(_fixture.Root, "SKIP"), "test")],
            2);

        var result = await _scanner.ScanAsync(config, null, CancellationToken.None);

        Assert.Equal(1, result.Files);
        Assert.Equal(1, result.ItemsExcluded);
    }

    [Fact]
    public async Task Nested_and_duplicate_roots_are_counted_once()
    {
        _fixture.AddFile("top.txt", "1");
        _fixture.AddFile(@"inner\a.txt", "12");
        var inner = Path.Combine(_fixture.Root, "inner");
        var config = new ScanConfiguration(
            [new ScanRoot(_fixture.Root, "t", "t"), new ScanRoot(inner + "\\", "t", "t"), new ScanRoot(_fixture.Root.ToUpperInvariant(), "t", "t")],
            [],
            2);

        var result = await _scanner.ScanAsync(config, null, CancellationToken.None);

        Assert.Equal(2, result.Files);
        Assert.Equal(3, result.Bytes);
    }

    [Fact]
    public async Task Disabled_root_is_not_scanned()
    {
        _fixture.AddFile("a.txt");

        var result = await _scanner.ScanAsync(
            new ScanConfiguration([new ScanRoot(_fixture.Root, "t", "t", Enabled: false)], [], 2), null, CancellationToken.None);

        Assert.Equal(0, result.Files);
    }

    [Fact]
    public async Task Cancellation_during_a_scan_ends_with_cancelled_status()
    {
        BuildTree();
        using var cancel = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cancel.Cancel());

        var result = await _scanner.ScanAsync(Config(), progress, cancel.Token);

        Assert.Equal(ScanStatus.Cancelled, result.Status);
        Assert.False(result.IsComplete);
        Assert.Equal(ScanStatus.Cancelled, (await _repository.GetSessionAsync(result.SessionId, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Database_failure_stops_the_scan_and_is_reported()
    {
        BuildTree();
        var scanner = new StorageScanner(new FailingItemsRepository(_repository));

        var result = await scanner.ScanAsync(Config() with { BatchSize = 5 }, null, CancellationToken.None);

        Assert.Equal(ScanStatus.Failed, result.Status);
        Assert.Equal("disk full", result.FailureReason);
    }

    [Fact]
    public async Task Scan_changes_nothing_on_disk()
    {
        BuildTree();
        var before = Snapshot();

        await _scanner.ScanAsync(Config(), null, CancellationToken.None);

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task New_scan_prunes_old_items_but_keeps_session_history_and_plan_references()
    {
        _fixture.AddFile("a.txt");
        var first = await _scanner.ScanAsync(Config(), null, CancellationToken.None);
        using var connection = _connections.Create();
        connection.Execute(
            """
            INSERT INTO CleanupPlans (CreatedAt, Status) VALUES ('t', 'Planned');
            INSERT INTO CleanupPlanItems (CleanupPlanId, ScanItemId, ValidationStatus)
            SELECT 1, Id, 'x' FROM ScanItems WHERE ItemType = 'File';
            """);

        var second = await _scanner.ScanAsync(Config(), null, CancellationToken.None);

        Assert.NotNull(await _repository.GetSessionAsync(first.SessionId, CancellationToken.None));
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ScanItems WHERE ScanSessionId = @Id", new { Id = first.SessionId }));
        Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ScanItems WHERE ScanSessionId = @Id", new { Id = second.SessionId }));
    }

    [Fact]
    public async Task Configuration_round_trips_and_required_exclusions_cannot_be_removed()
    {
        var defaults = new ScanConfiguration([new ScanRoot(@"C:\A", "t", "t")], [new ScanExclusion(@"C:\Data", "own data")], 2);
        var store = new ScanConfigurationStore(_connections, defaults);
        Assert.Same(defaults, await store.LoadAsync(CancellationToken.None));

        await store.SaveAsync(new ScanConfiguration([new ScanRoot(@"D:\B", "t", "t")], [], 3), CancellationToken.None);

        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(@"D:\B", Assert.Single(loaded.Roots).Path);
        Assert.Equal(@"C:\Data", Assert.Single(loaded.Exclusions).Path);
        Assert.Equal(3, loaded.MaxConcurrency);
    }

    [Fact]
    public void Defaults_exclude_own_data_and_keep_windows_temp_disabled()
    {
        var defaults = ScanDefaults.Create(@"D:\WCM");

        Assert.Equal(@"D:\WCM", Assert.Single(defaults.Exclusions).Path);
        Assert.False(defaults.Roots.Single(root => root.Category == "Windows temporary files").Enabled);
        Assert.DoesNotContain(defaults.Roots, root => root.Path.Contains("Documents", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Normalizer_trims_separators_and_expands_short_names()
    {
        var expected = PathNormalizer.Normalize(Path.GetTempPath());

        Assert.DoesNotContain("~", expected, StringComparison.Ordinal);
        Assert.Equal(expected, PathNormalizer.Normalize(expected + "\\"));
    }

    // Reads this PC's real folders into a temporary database. Run with WCM_REAL_SCAN=1.
    [Fact]
    public async Task Real_scan_of_default_roots()
    {
        if (Environment.GetEnvironmentVariable("WCM_REAL_SCAN") != "1")
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        var configuration = ScanDefaults.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsCleanupManager"));
        var result = await _scanner.ScanAsync(configuration, null, CancellationToken.None);

        _output.WriteLine($"REAL status={result.Status} files={result.Files} folders={result.Folders} bytes={result.Bytes} errors={result.Errors} links={result.LinksSkipped} excluded={result.ItemsExcluded} missing={result.MissingRoots} seconds={clock.Elapsed.TotalSeconds:N0} peakMB={Process.GetCurrentProcess().PeakWorkingSet64 >> 20} dbMB={new FileInfo(_connections.DatabasePath).Length >> 20} failure={result.FailureReason}");
        using var connection = _connections.Create();
        foreach (var row in connection.Query<(string Kind, long Count)>("SELECT Kind, COUNT(*) FROM ScanErrors GROUP BY Kind"))
        {
            _output.WriteLine($"REAL errors {row.Kind}={row.Count}");
        }

        foreach (var root in configuration.Roots.Where(r => r.Enabled))
        {
            var path = PathNormalizer.Normalize(root.Path);
            _output.WriteLine($"REAL root {root.Category} {path} bytes={connection.ExecuteScalar<long?>("SELECT SizeBytes FROM ScanItems WHERE Path = @path AND ItemType = 'Folder'", new { path })}");
        }

        Assert.NotEqual(ScanStatus.Failed, result.Status);
    }

    private static async Task RunCmd(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }

    private ScanConfiguration Config(int concurrency = 2) => new([new ScanRoot(_fixture.Root, "t", "t")], [], concurrency);

    private void BuildTree()
    {
        for (var folder = 0; folder < 10; folder++)
        {
            for (var file = 0; file < 20; file++)
            {
                _fixture.AddFile($@"dir{folder}\f{file}.bin", "12345");
            }
        }
    }

    private List<string> Snapshot() => Directory
        .EnumerateFileSystemEntries(_fixture.Root, "*", SearchOption.AllDirectories)
        .Select(path => $"{path}|{File.GetLastWriteTimeUtc(path):O}")
        .Order(StringComparer.Ordinal)
        .ToList();

    private sealed class InlineProgress(Action<ScanProgress> report) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => report(value);
    }

    private sealed class FailingItemsRepository(IScanRepository inner) : IScanRepository
    {
        public Task<long> StartSessionAsync(DateTimeOffset startedAt, CancellationToken cancellationToken) =>
            inner.StartSessionAsync(startedAt, cancellationToken);

        public Task AddItemsAsync(long sessionId, IEnumerable<ScanItemRecord> batch, CancellationToken cancellationToken) =>
            throw new IOException("disk full");

        public Task CompleteSessionAsync(long sessionId, DateTimeOffset completedAt, string status, long filesScanned, long foldersScanned, long errors, long totalBytes, long linksSkipped, CancellationToken cancellationToken) =>
            inner.CompleteSessionAsync(sessionId, completedAt, status, filesScanned, foldersScanned, errors, totalBytes, linksSkipped, cancellationToken);

        public Task<ScanSessionRecord?> GetSessionAsync(long sessionId, CancellationToken cancellationToken) =>
            inner.GetSessionAsync(sessionId, cancellationToken);

        public Task AddErrorsAsync(long sessionId, IEnumerable<ScanErrorRecord> errors, CancellationToken cancellationToken) =>
            inner.AddErrorsAsync(sessionId, errors, cancellationToken);

        public Task PruneItemsBeforeAsync(long sessionId, CancellationToken cancellationToken) =>
            inner.PruneItemsBeforeAsync(sessionId, cancellationToken);
    }
}
