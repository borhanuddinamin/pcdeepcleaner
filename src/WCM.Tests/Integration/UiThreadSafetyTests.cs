using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Infrastructure.FileSystem;
using WCM.Infrastructure.SQLite;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Integration;

/// <summary>A captured context stands in for the UI thread, which unit tests otherwise lack.</summary>
public sealed class UiThreadSafetyTests : IDisposable
{
    private readonly SyntheticDataFixture _fixture = new();
    private readonly SqliteConnectionFactory _connections;
    private readonly FakeUiContext _ui = new();

    public UiThreadSafetyTests()
    {
        _connections = new SqliteConnectionFactory(Path.Combine(_fixture.Root, "db", "cleanup.db"));
        new DatabaseInitializer(_connections).Initialize();
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Scanner_never_writes_to_the_database_on_the_callers_context()
    {
        _fixture.AddFile(@"tree\a.txt");
        var repository = new ContextRecordingScanRepository(new ScanRepository(_connections));
        var scanner = new StorageScanner(repository);
        var configuration = new ScanConfiguration([new ScanRoot(Path.Combine(_fixture.Root, "tree"), "t", "t")], [], 2, 1);

        var result = await RunOnFakeUi(() => scanner.ScanAsync(configuration, null, CancellationToken.None));

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.True(repository.Calls > 0);
        Assert.False(repository.CalledOnCapturedContext);
    }

    [Fact]
    public async Task Interrupted_scan_is_not_left_running()
    {
        var repository = new ScanRepository(_connections);
        var abandoned = await repository.StartSessionAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        var next = await repository.StartSessionAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(ScanStatus.Interrupted, (await repository.GetSessionAsync(abandoned, CancellationToken.None))!.Status);
        Assert.Equal(ScanStatus.Running, (await repository.GetSessionAsync(next, CancellationToken.None))!.Status);
    }

    private async Task<T> RunOnFakeUi<T>(Func<Task<T>> action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_ui);
        try
        {
            return await action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class FakeUiContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => ThreadPool.QueueUserWorkItem(_ =>
        {
            SetSynchronizationContext(this);
            d(state);
        });
    }

    private sealed class ContextRecordingScanRepository(IScanRepository inner) : IScanRepository
    {
        private int _calls;

        public int Calls => _calls;

        public bool CalledOnCapturedContext { get; private set; }

        public Task<long> StartSessionAsync(DateTimeOffset startedAt, CancellationToken cancellationToken)
        {
            Record();
            return inner.StartSessionAsync(startedAt, cancellationToken);
        }

        public Task AddItemsAsync(long sessionId, IEnumerable<ScanItemRecord> batch, CancellationToken cancellationToken)
        {
            Record();
            return inner.AddItemsAsync(sessionId, batch, cancellationToken);
        }

        public Task CompleteSessionAsync(long sessionId, DateTimeOffset completedAt, string status, long filesScanned, long foldersScanned, long errors, long totalBytes, long linksSkipped, CancellationToken cancellationToken)
        {
            Record();
            return inner.CompleteSessionAsync(sessionId, completedAt, status, filesScanned, foldersScanned, errors, totalBytes, linksSkipped, cancellationToken);
        }

        public Task<ScanSessionRecord?> GetSessionAsync(long sessionId, CancellationToken cancellationToken) =>
            inner.GetSessionAsync(sessionId, cancellationToken);

        public Task AddErrorsAsync(long sessionId, IEnumerable<ScanErrorRecord> errors, CancellationToken cancellationToken)
        {
            Record();
            return inner.AddErrorsAsync(sessionId, errors, cancellationToken);
        }

        public Task PruneItemsBeforeAsync(long sessionId, CancellationToken cancellationToken)
        {
            Record();
            return inner.PruneItemsBeforeAsync(sessionId, cancellationToken);
        }

        private void Record()
        {
            Interlocked.Increment(ref _calls);
            if (SynchronizationContext.Current is FakeUiContext)
            {
                CalledOnCapturedContext = true;
            }
        }
    }
}
