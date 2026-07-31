using Microsoft.Data.Sqlite;
using SnapdexCore.Indexing;

namespace SnapdexCore.Tests;

public class LibraryIndexerTests
{
    [Fact]
    public void LibraryScanner_YieldsSupportedFilesFromFixture()
    {
        var fixtureRoot = GetScanFixtureRoot();
        var scanner = new LibraryScanner();

        var files = scanner.Scan(fixtureRoot).ToList();

        Assert.Equal(5, files.Count);
        Assert.All(files, f => Assert.True(Path.IsPathRooted(f.Path)));
        Assert.Contains(files, f => f.Filename.Equals("raw.cr2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IndexFolder_CreatesSchemaAndUpsertsByPath()
    {
        var fixtureRoot = GetScanFixtureRoot();
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

    [Fact]
    public void IndexFolder_ExtractsExifMetadata_AndStoresNullableFields()
    {
        var fixtureRoot = GetExifFixtureRoot();
        var dbPath = Path.Combine(Path.GetTempPath(), $"snapdex-exif-tests-{Guid.NewGuid():N}.db");

        try
        {
            var indexer = new LibraryIndexer(dbPath);
            var indexed = indexer.IndexFolder(fixtureRoot);

            Assert.Equal(3, indexed);

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    filename,
                    camera_make,
                    camera_model,
                    lens_model,
                    iso,
                    aperture,
                    shutter_seconds,
                    focal_length_mm,
                    captured_at,
                    gps_latitude,
                    gps_longitude
                FROM images
                ORDER BY filename;
                """;

            using var reader = command.ExecuteReader();
            var rows = new List<IndexedRow>();
            while (reader.Read())
            {
                rows.Add(new IndexedRow(
                    reader.GetString(0),
                    DbString(reader, 1),
                    DbString(reader, 2),
                    DbString(reader, 3),
                    DbInt(reader, 4),
                    DbDouble(reader, 5),
                    DbDouble(reader, 6),
                    DbDouble(reader, 7),
                    DbString(reader, 8),
                    DbDouble(reader, 9),
                    DbDouble(reader, 10)));
            }

            Assert.Equal(3, rows.Count);

            var withExif = rows.Single(r => r.Filename == "with-exif.jpg");
            Assert.Equal("Canon", withExif.CameraMake);
            Assert.Equal("EOS R6", withExif.CameraModel);
            Assert.Equal("EF35mm f/1.4L II USM", withExif.LensModel);
            Assert.Equal(400, withExif.Iso);
            Assert.InRange(withExif.Aperture!.Value, 2.799, 2.801);
            Assert.InRange(withExif.ShutterSeconds!.Value, 0.0079, 0.0081);
            Assert.InRange(withExif.FocalLengthMm!.Value, 34.99, 35.01);

            var capturedAt = DateTimeOffset.Parse(withExif.CapturedAt!, null, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.Equal(2024, capturedAt.Year);
            Assert.Equal(5, capturedAt.Month);
            Assert.Equal(6, capturedAt.Day);
            Assert.Equal(7, capturedAt.Hour);
            Assert.Equal(8, capturedAt.Minute);
            Assert.Equal(9, capturedAt.Second);

            Assert.InRange(withExif.GpsLatitude!.Value, 37.7748, 37.7750);
            Assert.InRange(withExif.GpsLongitude!.Value, -122.4195, -122.4193);

            var partial = rows.Single(r => r.Filename == "partial-exif.jpg");
            Assert.Equal("Sony", partial.CameraMake);
            Assert.Equal("ILCE-7M4", partial.CameraModel);
            Assert.Null(partial.LensModel);
            Assert.Null(partial.Iso);
            Assert.Null(partial.Aperture);
            Assert.Null(partial.ShutterSeconds);
            Assert.Null(partial.FocalLengthMm);
            Assert.Null(partial.CapturedAt);
            Assert.Null(partial.GpsLatitude);
            Assert.Null(partial.GpsLongitude);

            var withoutExif = rows.Single(r => r.Filename == "without-exif.jpg");
            Assert.Null(withoutExif.CameraMake);
            Assert.Null(withoutExif.CameraModel);
            Assert.Null(withoutExif.LensModel);
            Assert.Null(withoutExif.Iso);
            Assert.Null(withoutExif.Aperture);
            Assert.Null(withoutExif.ShutterSeconds);
            Assert.Null(withoutExif.FocalLengthMm);
            Assert.Null(withoutExif.CapturedAt);
            Assert.Null(withoutExif.GpsLatitude);
            Assert.Null(withoutExif.GpsLongitude);
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

    private static string? DbString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? DbInt(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static double? DbDouble(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private sealed record IndexedRow(
        string Filename,
        string? CameraMake,
        string? CameraModel,
        string? LensModel,
        int? Iso,
        double? Aperture,
        double? ShutterSeconds,
        double? FocalLengthMm,
        string? CapturedAt,
        double? GpsLatitude,
        double? GpsLongitude);
}
