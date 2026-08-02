using SnapdexCore.Indexing;
using SnapdexCore.Search;

namespace SnapdexCore.Tests;

public class SearchAndIndexingCallbacksTests
{
    [Fact]
    public void IndexFolder_InvokesOnImageIndexedCallback_ForEachIndexedImage()
    {
        var fixtureRoot = GetScanFixtureRoot();
        var dbPath = Path.Combine(Path.GetTempPath(), $"snapdex-callback-tests-{Guid.NewGuid():N}.db");

        try
        {
            var callbacks = 0;
            var indexer = new LibraryIndexer(dbPath);
            var indexed = indexer.IndexFolder(fixtureRoot, _ => callbacks++);

            Assert.Equal(5, indexed);
            Assert.Equal(5, callbacks);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public void Search_ReturnsRowsMatchingQueryTranslatorFilters()
    {
        var fixtureRoot = GetExifFixtureRoot();
        var dbPath = Path.Combine(Path.GetTempPath(), $"snapdex-search-tests-{Guid.NewGuid():N}.db");

        try
        {
            var indexer = new LibraryIndexer(dbPath);
            indexer.IndexFolder(fixtureRoot);

            using var index = new SqliteImageIndex(dbPath);
            var parser = new SearchQueryParser();
            var translator = new SqliteQueryTranslator();

            var parsed = parser.Parse("camera:Canon");
            Assert.True(parsed.Success, parsed.Error);

            var translation = translator.Translate(parsed.Query!);
            var rows = index.Search(translation);

            var match = Assert.Single(rows);
            Assert.Equal("with-exif.jpg", match.Filename);
            Assert.Equal("Canon", match.CameraMake);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private static string GetScanFixtureRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "scan-fixture");
        Assert.True(Directory.Exists(root), $"Fixture folder missing: {root}");
        return root;
    }

    private static string GetExifFixtureRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "exif-fixture");
        Assert.True(Directory.Exists(root), $"Fixture folder missing: {root}");
        return root;
    }
}
