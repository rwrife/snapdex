using SnapdexCore.Indexing;

namespace SnapdexCore.Tests;

public class IncrementalIndexingServiceTests
{
    [Fact]
    public void ReconcileFolders_SyncsAddMoveModifyAndDeleteChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), $"snapdex-incremental-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(Path.GetTempPath(), $"snapdex-incremental-{Guid.NewGuid():N}.db");

        Directory.CreateDirectory(root);

        var fileA = Path.Combine(root, "a.jpg");
        var fileB = Path.Combine(root, "b.jpg");

        File.WriteAllBytes(fileA, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(fileB, new byte[] { 4, 5, 6 });

        try
        {
            using var service = new IncrementalIndexingService(
                dbPath,
                scanner: new LibraryScanner(new StubMetadataReader()),
                debounceInterval: TimeSpan.FromMilliseconds(20));

            var first = service.ReconcileFolders(new[] { root });
            Assert.Equal(2, first.UpsertedCount);
            Assert.Equal(0, first.DeletedCount);
            Assert.Equal(2, first.ScannedCount);

            var movedB = Path.Combine(root, "b-renamed.jpg");
            File.Move(fileB, movedB);
            File.AppendAllText(movedB, "-modified");
            File.SetLastWriteTimeUtc(movedB, DateTime.UtcNow.AddMinutes(1));

            File.Delete(fileA);

            var fileC = Path.Combine(root, "c.jpg");
            File.WriteAllBytes(fileC, new byte[] { 7, 8, 9 });

            var second = service.ReconcileFolders(new[] { root });
            Assert.Equal(2, second.UpsertedCount);
            Assert.Equal(2, second.DeletedCount);
            Assert.Equal(2, second.ScannedCount);

            File.AppendAllText(fileC, "-changed");
            File.SetLastWriteTimeUtc(fileC, DateTime.UtcNow.AddMinutes(2));

            var third = service.ReconcileFolders(new[] { root });
            Assert.Equal(1, third.UpsertedCount);
            Assert.Equal(0, third.DeletedCount);
            Assert.Equal(2, third.ScannedCount);

            using var index = new SqliteImageIndex(dbPath);
            var states = index.GetIndexedImageStates();

            Assert.False(states.ContainsKey(Path.GetFullPath(fileA)));
            Assert.False(states.ContainsKey(Path.GetFullPath(fileB)));
            Assert.True(states.ContainsKey(Path.GetFullPath(movedB)));
            Assert.True(states.ContainsKey(Path.GetFullPath(fileC)));
            Assert.Equal(2, states.Count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public void FlushPendingChanges_DebouncesBurstEvents_AndAppliesLatestState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"snapdex-debounce-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(Path.GetTempPath(), $"snapdex-debounce-{Guid.NewGuid():N}.db");

        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "burst.jpg");
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });

        try
        {
            using var service = new IncrementalIndexingService(
                dbPath,
                scanner: new LibraryScanner(new StubMetadataReader()),
                debounceInterval: TimeSpan.FromMilliseconds(50));

            service.NotifyPathChanged(filePath, IncrementalChangeKind.Upsert);
            service.NotifyPathChanged(filePath, IncrementalChangeKind.Upsert);
            service.NotifyPathChanged(filePath, IncrementalChangeKind.Upsert);

            var firstFlush = service.FlushPendingChanges();
            Assert.Equal(1, firstFlush.UpsertedCount);
            Assert.Equal(0, firstFlush.DeletedCount);

            service.NotifyPathChanged(filePath, IncrementalChangeKind.Delete);
            service.NotifyPathChanged(filePath, IncrementalChangeKind.Upsert);

            var secondFlush = service.FlushPendingChanges();
            Assert.Equal(1, secondFlush.UpsertedCount);
            Assert.Equal(0, secondFlush.DeletedCount);

            service.NotifyPathChanged(filePath, IncrementalChangeKind.Delete);
            service.NotifyPathChanged(filePath, IncrementalChangeKind.Delete);

            var thirdFlush = service.FlushPendingChanges();
            Assert.Equal(0, thirdFlush.UpsertedCount);
            Assert.Equal(1, thirdFlush.DeletedCount);

            using var index = new SqliteImageIndex(dbPath);
            Assert.Equal(0, index.CountImages());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private sealed class StubMetadataReader : IImageMetadataReader
    {
        public ImageMetadata Read(string filePath) => new();
    }
}
