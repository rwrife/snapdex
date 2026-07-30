using SnapdexCore.Indexing;

namespace SnapdexCore.Tests;

public class LibraryIndexerTests
{
    [Fact]
    public void LibraryScanner_YieldsSupportedFilesFromFixture()
    {
        var fixtureRoot = GetFixtureRoot();
        var scanner = new LibraryScanner();

        var files = scanner.Scan(fixtureRoot).ToList();

        Assert.Equal(5, files.Count);
        Assert.All(files, f => Assert.True(Path.IsPathRooted(f.Path)));
        Assert.Contains(files, f => f.Filename.Equals("raw.cr2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IndexFolder_CreatesSchemaAndUpsertsByPath()
    {
        var fixtureRoot = GetFixtureRoot();
        var dbPath = Path.Combine(Path.GetTempPath(), $"snapdex-tests-{Guid.NewGuid():N}.db");

        try
        {
            var indexer = new LibraryIndexer(dbPath);

            var firstPass = indexer.IndexFolder(fixtureRoot);
            var secondPass = indexer.IndexFolder(fixtureRoot);

            Assert.Equal(5, firstPass);
            Assert.Equal(5, secondPass);

            using var index = new SqliteImageIndex(dbPath);
            Assert.True(index.ImagesTableExists());
            Assert.Equal(5, index.CountImages());
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private static string GetFixtureRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "scan-fixture");
        Assert.True(Directory.Exists(root), $"Fixture folder missing: {root}");
        return root;
    }
}
