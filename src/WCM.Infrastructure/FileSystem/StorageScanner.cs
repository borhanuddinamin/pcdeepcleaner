using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Threading.Channels;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;

namespace WCM.Infrastructure.FileSystem;

/// <summary>
/// Streams metadata into the database through a bounded channel, so memory use does not grow with
/// the number of files. Reparse points are recorded and never entered. Nothing is modified.
/// </summary>
public sealed class StorageScanner(IScanRepository repository) : IStorageScanner
{
    private const int MaxDepth = 256;
    private const int MaxStoredErrors = 5000;
    private const long ProgressIntervalMs = 200;

    // Task.Run keeps every continuation, including database writes, off the UI thread.
    public Task<ScanResult> ScanAsync(
        ScanConfiguration configuration,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => ScanCoreAsync(configuration, progress, cancellationToken), CancellationToken.None);

    private async Task<ScanResult> ScanCoreAsync(
        ScanConfiguration configuration,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessionId = await repository.StartSessionAsync(DateTimeOffset.UtcNow, cancellationToken);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var batchSize = Math.Max(1, configuration.BatchSize);
        var channel = Channel.CreateBounded<ScanItemRecord>(new BoundedChannelOptions(batchSize * 4)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

        var walk = new Walk(configuration, channel.Writer, progress, stop.Token);
        var writer = WriteBatchesAsync(sessionId, channel.Reader, batchSize, stop);
        string? failure = null;

        try
        {
            await Task.Run(walk.Run, CancellationToken.None);
        }
        catch (Exception ex) when (IsCancellation(ex))
        {
            // Reported through the status below.
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        channel.Writer.TryComplete();
        failure = await writer ?? failure;

        var status = failure is not null ? ScanStatus.Failed
            : cancellationToken.IsCancellationRequested ? ScanStatus.Cancelled
            : walk.Errors > 0 ? ScanStatus.CompletedWithErrors
            : ScanStatus.Completed;

        // Results are saved even after cancellation, so the token is not used from here.
        if (!walk.StoredErrors.IsEmpty)
        {
            await repository.AddErrorsAsync(sessionId, walk.StoredErrors, CancellationToken.None);
        }

        await repository.CompleteSessionAsync(
            sessionId, DateTimeOffset.UtcNow, status, walk.Files, walk.Folders, walk.Errors, walk.Bytes, walk.Links, CancellationToken.None);

        if (status is ScanStatus.Completed or ScanStatus.CompletedWithErrors)
        {
            await repository.PruneItemsBeforeAsync(sessionId, CancellationToken.None);
        }

        walk.ReportNow(null);
        return new ScanResult(sessionId, status, walk.Files, walk.Folders, walk.Errors, walk.Bytes, walk.Links, walk.Excluded, walk.MissingRoots, failure);
    }

    private static bool IsCancellation(Exception ex) => ex switch
    {
        OperationCanceledException => true,
        AggregateException aggregate => aggregate.Flatten().InnerExceptions.All(IsCancellation),
        _ => false,
    };

    // Returns the failure message, and stops the walk if the database can't keep up or fails.
    private async Task<string?> WriteBatchesAsync(
        long sessionId,
        ChannelReader<ScanItemRecord> reader,
        int batchSize,
        CancellationTokenSource stop)
    {
        await Task.Yield();
        var batch = new List<ScanItemRecord>(batchSize);
        try
        {
            await foreach (var item in reader.ReadAllAsync(CancellationToken.None))
            {
                batch.Add(item);
                if (batch.Count >= batchSize)
                {
                    await repository.AddItemsAsync(sessionId, batch, CancellationToken.None);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await repository.AddItemsAsync(sessionId, batch, CancellationToken.None);
            }

            return null;
        }
        catch (Exception ex)
        {
            await stop.CancelAsync();
            return ex.Message;
        }
    }

    private readonly record struct Entry(
        string Path,
        FileAttributes Attributes,
        bool IsDirectory,
        long Length,
        DateTimeOffset LastWrite,
        DateTimeOffset Created);

    private readonly record struct Totals(long Bytes, long Items, bool Partial)
    {
        public static Totals operator +(Totals a, Totals b) =>
            new(a.Bytes + b.Bytes, a.Items + b.Items, a.Partial || b.Partial);
    }

    private sealed class Walk
    {
        private static readonly EnumerationOptions Options = new()
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
        };

        private readonly ScanConfiguration _configuration;
        private readonly ChannelWriter<ScanItemRecord> _writer;
        private readonly IProgress<ScanProgress>? _progress;
        private readonly CancellationToken _token;
        private readonly SemaphoreSlim _extraWorkers;
        private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _nextReport;
        private long _files;
        private long _folders;
        private long _errors;
        private long _bytes;
        private long _links;
        private long _excludedItems;

        public Walk(
            ScanConfiguration configuration,
            ChannelWriter<ScanItemRecord> writer,
            IProgress<ScanProgress>? progress,
            CancellationToken token)
        {
            _configuration = configuration;
            _writer = writer;
            _progress = progress;
            _token = token;
            _extraWorkers = new SemaphoreSlim(Math.Max(0, configuration.MaxConcurrency - 1));

            foreach (var exclusion in configuration.Exclusions)
            {
                _excluded.Add(PathNormalizer.Normalize(exclusion.Path));
            }
        }

        public ConcurrentQueue<ScanErrorRecord> StoredErrors { get; } = new();

        public int MissingRoots { get; private set; }

        public long Files => Interlocked.Read(ref _files);

        public long Folders => Interlocked.Read(ref _folders);

        public long Errors => Interlocked.Read(ref _errors);

        public long Bytes => Interlocked.Read(ref _bytes);

        public long Links => Interlocked.Read(ref _links);

        public long Excluded => Interlocked.Read(ref _excludedItems);

        public void Run()
        {
            var roots = _configuration.Roots
                .Where(root => root.Enabled)
                .Select(root => PathNormalizer.Normalize(root.Path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // A root inside another root is scanned once, on its own, so nothing is counted twice.
            var nestedRoots = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots.Where(root => !_excluded.Contains(root)))
            {
                _token.ThrowIfCancellationRequested();
                ScanRootFolder(root, nestedRoots);
            }
        }

        public void ReportNow(string? path) =>
            _progress?.Report(new ScanProgress(Files, Folders, Errors, Bytes, path));

        private void ScanRootFolder(string root, HashSet<string> nestedRoots)
        {
            DirectoryInfo info;
            try
            {
                info = new DirectoryInfo(root);
                if (!info.Exists)
                {
                    MissingRoots++;
                    return;
                }

                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Interlocked.Increment(ref _links);
                    Write(new ScanItemRecord(root, ScanItemType.Link, 0, info.LastWriteTimeUtc, false, false, Attributes: (int)info.Attributes));
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                RecordError(root, ex);
                return;
            }

            ScanFolder(
                new Entry(root, info.Attributes, true, 0, info.LastWriteTimeUtc, info.CreationTimeUtc),
                nestedRoots,
                0);
        }

        private Totals ScanFolder(Entry folder, HashSet<string> nestedRoots, int depth)
        {
            _token.ThrowIfCancellationRequested();
            var totals = new Totals(0, 0, false);
            var subfolders = new List<Entry>();

            if (depth >= MaxDepth)
            {
                RecordError(folder.Path, new PathTooLongException("The folder is nested too deeply to scan."));
                totals = totals with { Partial = true };
            }
            else
            {
                totals += ReadEntries(folder.Path, subfolders, nestedRoots);
            }

            var running = new List<Task<Totals>>();
            foreach (var subfolder in subfolders)
            {
                if (_extraWorkers.Wait(0))
                {
                    running.Add(Task.Run(
                        () =>
                        {
                            try
                            {
                                return ScanFolder(subfolder, nestedRoots, depth + 1);
                            }
                            finally
                            {
                                _extraWorkers.Release();
                            }
                        },
                        CancellationToken.None));
                }
                else
                {
                    totals += ScanFolder(subfolder, nestedRoots, depth + 1);
                }
            }

            foreach (var task in running)
            {
                totals += task.GetAwaiter().GetResult();
            }

            Interlocked.Increment(ref _folders);
            Write(new ScanItemRecord(
                folder.Path,
                ScanItemType.Folder,
                totals.Bytes,
                folder.LastWrite,
                false,
                false,
                totals.Items,
                (int)folder.Attributes,
                folder.Created,
                totals.Partial));

            return totals with { Items = totals.Items + 1 };
        }

        private Totals ReadEntries(string path, List<Entry> subfolders, HashSet<string> nestedRoots)
        {
            var bytes = 0L;
            var items = 0L;
            var partial = false;

            try
            {
                var entries = new FileSystemEnumerable<Entry>(
                    path,
                    (ref FileSystemEntry entry) => new Entry(
                        entry.ToFullPath(),
                        entry.Attributes,
                        entry.IsDirectory,
                        entry.IsDirectory ? 0 : entry.Length,
                        entry.LastWriteTimeUtc,
                        entry.CreationTimeUtc),
                    Options);

                foreach (var entry in entries)
                {
                    _token.ThrowIfCancellationRequested();

                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        Interlocked.Increment(ref _links);
                        Write(new ScanItemRecord(entry.Path, ScanItemType.Link, 0, entry.LastWrite, false, false, Attributes: (int)entry.Attributes, CreatedAt: entry.Created));
                        items++;
                    }
                    else if (entry.IsDirectory)
                    {
                        if (_excluded.Contains(entry.Path))
                        {
                            Interlocked.Increment(ref _excludedItems);
                            Write(new ScanItemRecord(entry.Path, ScanItemType.Excluded, 0, entry.LastWrite, false, true, Attributes: (int)entry.Attributes, CreatedAt: entry.Created));
                        }
                        else if (!nestedRoots.Contains(entry.Path))
                        {
                            subfolders.Add(entry);
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref _files);
                        Interlocked.Add(ref _bytes, entry.Length);
                        Write(new ScanItemRecord(entry.Path, ScanItemType.File, entry.Length, entry.LastWrite, false, false, Attributes: (int)entry.Attributes, CreatedAt: entry.Created));
                        bytes += entry.Length;
                        items++;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                RecordError(path, ex);
                partial = true;
            }

            MaybeReport(path);
            return new Totals(bytes, items, partial);
        }

        private void Write(ScanItemRecord item)
        {
            // TryWrite fails only when the channel is full, which slows the walk to the database's pace.
            if (!_writer.TryWrite(item))
            {
                _writer.WriteAsync(item, _token).AsTask().GetAwaiter().GetResult();
            }
        }

        private void RecordError(string path, Exception ex)
        {
            Interlocked.Increment(ref _errors);
            if (StoredErrors.Count < MaxStoredErrors)
            {
                var kind = ex switch
                {
                    UnauthorizedAccessException or System.Security.SecurityException => "AccessDenied",
                    DirectoryNotFoundException or FileNotFoundException => "Disappeared",
                    PathTooLongException => "TooDeep",
                    _ => "IOError",
                };
                StoredErrors.Enqueue(new ScanErrorRecord(path, kind, ex.Message));
            }
        }

        private void MaybeReport(string path)
        {
            var now = _clock.ElapsedMilliseconds;
            var due = Interlocked.Read(ref _nextReport);
            if (_progress is not null && now >= due
                && Interlocked.CompareExchange(ref _nextReport, now + ProgressIntervalMs, due) == due)
            {
                ReportNow(path);
            }
        }
    }
}
