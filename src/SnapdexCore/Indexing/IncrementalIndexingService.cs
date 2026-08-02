namespace SnapdexCore.Indexing;

public enum IncrementalChangeKind
{
    Upsert,
    Delete
}

public sealed record IncrementalFlushResult(int UpsertedCount, int DeletedCount);

public sealed record IncrementalSyncResult(int UpsertedCount, int DeletedCount, int ScannedCount);

public sealed class IncrementalIndexingService : IDisposable
{
    private static readonly NotifyFilters WatcherNotifyFilters =
        NotifyFilters.FileName |
        NotifyFilters.DirectoryName |
        NotifyFilters.Size |
        NotifyFilters.LastWrite |
        NotifyFilters.CreationTime;

    private readonly string _databasePath;
    private readonly LibraryScanner _scanner;
    private readonly TimeSpan _debounceInterval;
    private readonly object _sync = new();
    private readonly HashSet<string> _pendingUpserts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingDeletes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Timer _debounceTimer;

    private bool _disposed;

    public IncrementalIndexingService(
        string databasePath,
        LibraryScanner? scanner = null,
        TimeSpan? debounceInterval = null)
    {
        _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        _scanner = scanner ?? new LibraryScanner();
        _debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(500);
        _debounceTimer = new Timer(_ => SafeFlushFromTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void StartWatching(IEnumerable<string> rootPaths)
    {
        ThrowIfDisposed();

        var normalizedRoots = NormalizeRootPaths(rootPaths)
            .Where(Directory.Exists)
            .ToList();

        StopWatching();

        foreach (var rootPath in normalizedRoots)
        {
            var watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                Filter = "*.*",
                NotifyFilter = WatcherNotifyFilters,
                EnableRaisingEvents = false
            };

            watcher.Created += OnCreatedOrChanged;
            watcher.Changed += OnCreatedOrChanged;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnWatcherError;

            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    public void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnCreatedOrChanged;
            watcher.Changed -= OnCreatedOrChanged;
            watcher.Deleted -= OnDeleted;
            watcher.Renamed -= OnRenamed;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    public void NotifyPathChanged(string filePath, IncrementalChangeKind changeKind)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(filePath);

        lock (_sync)
        {
            if (changeKind == IncrementalChangeKind.Upsert)
            {
                if (!LibraryScanner.IsSupportedImagePath(fullPath))
                {
                    return;
                }

                _pendingDeletes.Remove(fullPath);
                _pendingUpserts.Add(fullPath);
            }
            else
            {
                _pendingUpserts.Remove(fullPath);
                _pendingDeletes.Add(fullPath);
            }

            _debounceTimer.Change(_debounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    public IncrementalFlushResult FlushPendingChanges(Action<ScannedImageFile>? onImageIndexed = null)
    {
        ThrowIfDisposed();

        string[] upserts;
        string[] deletes;

        lock (_sync)
        {
            _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            upserts = _pendingUpserts.ToArray();
            deletes = _pendingDeletes.ToArray();
            _pendingUpserts.Clear();
            _pendingDeletes.Clear();
        }

        if (upserts.Length == 0 && deletes.Length == 0)
        {
            return new IncrementalFlushResult(0, 0);
        }

        using var index = new SqliteImageIndex(_databasePath);
        index.EnsureCreated();

        var upsertedCount = 0;
        var deletedCount = 0;

        foreach (var path in deletes)
        {
            if (index.DeleteImageByPath(path) > 0)
            {
                deletedCount++;
            }
        }

        foreach (var path in upserts)
        {
            if (!File.Exists(path))
            {
                if (index.DeleteImageByPath(path) > 0)
                {
                    deletedCount++;
                }

                continue;
            }

            if (!_scanner.TryScanFile(path, out var image) || image is null)
            {
                continue;
            }

            index.UpsertImage(image);
            onImageIndexed?.Invoke(image);
            upsertedCount++;
        }

        return new IncrementalFlushResult(upsertedCount, deletedCount);
    }

    public IncrementalSyncResult ReconcileFolders(
        IEnumerable<string> rootPaths,
        Action<ScannedImageFile>? onImageIndexed = null)
    {
        ThrowIfDisposed();

        var normalizedRoots = NormalizeRootPaths(rootPaths)
            .Where(Directory.Exists)
            .ToList();

        if (normalizedRoots.Count == 0)
        {
            return new IncrementalSyncResult(0, 0, 0);
        }

        using var index = new SqliteImageIndex(_databasePath);
        index.EnsureCreated();

        var existing = index.GetIndexedImageStates();
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var upsertedCount = 0;

        foreach (var root in normalizedRoots)
        {
            foreach (var image in _scanner.Scan(root))
            {
                scannedPaths.Add(image.Path);

                if (existing.TryGetValue(image.Path, out var state)
                    && state.Size == image.Size
                    && state.ModifiedTimeUtc == image.ModifiedTimeUtc)
                {
                    continue;
                }

                index.UpsertImage(image);
                onImageIndexed?.Invoke(image);
                upsertedCount++;
            }
        }

        var deletedCount = 0;
        foreach (var indexedState in existing.Values)
        {
            if (!IsUnderAnyRoot(indexedState.Path, normalizedRoots))
            {
                continue;
            }

            if (scannedPaths.Contains(indexedState.Path))
            {
                continue;
            }

            if (index.DeleteImageByPath(indexedState.Path) > 0)
            {
                deletedCount++;
            }
        }

        return new IncrementalSyncResult(upsertedCount, deletedCount, scannedPaths.Count);
    }

    private void OnCreatedOrChanged(object sender, FileSystemEventArgs e)
        => NotifyPathChanged(e.FullPath, IncrementalChangeKind.Upsert);

    private void OnDeleted(object sender, FileSystemEventArgs e)
        => NotifyPathChanged(e.FullPath, IncrementalChangeKind.Delete);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        NotifyPathChanged(e.OldFullPath, IncrementalChangeKind.Delete);
        NotifyPathChanged(e.FullPath, IncrementalChangeKind.Upsert);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (sender is not FileSystemWatcher watcher)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(watcher.Path))
        {
            _ = ReconcileFolders(new[] { watcher.Path });
        }
    }

    private void SafeFlushFromTimer()
    {
        try
        {
            FlushPendingChanges();
        }
        catch
        {
            // Best-effort background processing. The UI can still trigger manual refresh/reconcile.
        }
    }

    private static List<string> NormalizeRootPaths(IEnumerable<string> rootPaths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootPath in rootPaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                continue;
            }

            set.Add(Path.GetFullPath(rootPath));
        }

        return set.ToList();
    }

    private static bool IsUnderAnyRoot(string path, IReadOnlyList<string> roots)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var root in roots)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (normalizedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(IncrementalIndexingService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopWatching();
        _debounceTimer.Dispose();
        _disposed = true;
    }
}
