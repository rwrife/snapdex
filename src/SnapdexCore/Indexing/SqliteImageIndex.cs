using System.Globalization;
using Microsoft.Data.Sqlite;
using SnapdexCore.Search;

namespace SnapdexCore.Indexing;

public sealed class SqliteImageIndex : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteImageIndex(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection($"Data Source={fullPath}");
        _connection.Open();
    }

    public void EnsureCreated()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS images (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path TEXT NOT NULL UNIQUE,
                filename TEXT NOT NULL,
                size INTEGER NOT NULL,
                mtime TEXT NOT NULL,
                indexed_at TEXT NOT NULL,
                camera_make TEXT NULL,
                camera_model TEXT NULL,
                lens_model TEXT NULL,
                iso INTEGER NULL,
                aperture REAL NULL,
                shutter_seconds REAL NULL,
                focal_length_mm REAL NULL,
                captured_at TEXT NULL,
                gps_latitude REAL NULL,
                gps_longitude REAL NULL
            );
            """;
        command.ExecuteNonQuery();

        EnsureColumnExists("camera_make", "TEXT NULL");
        EnsureColumnExists("camera_model", "TEXT NULL");
        EnsureColumnExists("lens_model", "TEXT NULL");
        EnsureColumnExists("iso", "INTEGER NULL");
        EnsureColumnExists("aperture", "REAL NULL");
        EnsureColumnExists("shutter_seconds", "REAL NULL");
        EnsureColumnExists("focal_length_mm", "REAL NULL");
        EnsureColumnExists("captured_at", "TEXT NULL");
        EnsureColumnExists("gps_latitude", "REAL NULL");
        EnsureColumnExists("gps_longitude", "REAL NULL");
    }

    public void UpsertImage(ScannedImageFile image)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO images (
                path,
                filename,
                size,
                mtime,
                indexed_at,
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
            )
            VALUES (
                $path,
                $filename,
                $size,
                $mtime,
                $indexedAt,
                $cameraMake,
                $cameraModel,
                $lensModel,
                $iso,
                $aperture,
                $shutterSeconds,
                $focalLengthMm,
                $capturedAt,
                $gpsLatitude,
                $gpsLongitude
            )
            ON CONFLICT(path) DO UPDATE SET
                filename = excluded.filename,
                size = excluded.size,
                mtime = excluded.mtime,
                indexed_at = excluded.indexed_at,
                camera_make = excluded.camera_make,
                camera_model = excluded.camera_model,
                lens_model = excluded.lens_model,
                iso = excluded.iso,
                aperture = excluded.aperture,
                shutter_seconds = excluded.shutter_seconds,
                focal_length_mm = excluded.focal_length_mm,
                captured_at = excluded.captured_at,
                gps_latitude = excluded.gps_latitude,
                gps_longitude = excluded.gps_longitude;
            """;

        command.Parameters.AddWithValue("$path", image.Path);
        command.Parameters.AddWithValue("$filename", image.Filename);
        command.Parameters.AddWithValue("$size", image.Size);
        command.Parameters.AddWithValue("$mtime", image.ModifiedTimeUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$indexedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$cameraMake", DbValue(image.CameraMake));
        command.Parameters.AddWithValue("$cameraModel", DbValue(image.CameraModel));
        command.Parameters.AddWithValue("$lensModel", DbValue(image.LensModel));
        command.Parameters.AddWithValue("$iso", DbValue(image.Iso));
        command.Parameters.AddWithValue("$aperture", DbValue(image.Aperture));
        command.Parameters.AddWithValue("$shutterSeconds", DbValue(image.ShutterSeconds));
        command.Parameters.AddWithValue("$focalLengthMm", DbValue(image.FocalLengthMm));
        command.Parameters.AddWithValue("$capturedAt", DbValue(image.CapturedAtUtc?.UtcDateTime.ToString("O")));
        command.Parameters.AddWithValue("$gpsLatitude", DbValue(image.GpsLatitude));
        command.Parameters.AddWithValue("$gpsLongitude", DbValue(image.GpsLongitude));

        command.ExecuteNonQuery();
    }

    public IReadOnlyList<IndexedImageRecord> Search(SqliteQueryTranslation translation, int limit = 10000)
    {
        ArgumentNullException.ThrowIfNull(translation);

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Search limit must be greater than zero.");
        }

        var sql = translation.Sql.Trim();
        if (sql.EndsWith(';'))
        {
            sql = sql[..^1];
        }

        using var command = _connection.CreateCommand();
        command.CommandText = $"{sql} LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);

        foreach (var (name, value) in translation.Parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        using var reader = command.ExecuteReader();
        var results = new List<IndexedImageRecord>();
        while (reader.Read())
        {
            results.Add(new IndexedImageRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                ParseUtc(reader.GetString(3)),
                ParseUtc(reader.GetString(4)),
                DbString(reader, 5),
                DbString(reader, 6),
                DbString(reader, 7),
                DbInt(reader, 8),
                DbDouble(reader, 9),
                DbDouble(reader, 10),
                DbDouble(reader, 11),
                DbDateTimeOffset(reader, 12),
                DbDouble(reader, 13),
                DbDouble(reader, 14)));
        }

        return results;
    }

    public IReadOnlyDictionary<string, IndexedImageState> GetIndexedImageStates()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT path, size, mtime FROM images;";

        using var reader = command.ExecuteReader();
        var states = new Dictionary<string, IndexedImageState>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var path = reader.GetString(0);
            var size = reader.GetInt64(1);
            var modified = ParseUtc(reader.GetString(2));
            states[path] = new IndexedImageState(path, size, modified);
        }

        return states;
    }

    public int DeleteImageByPath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return 0;
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM images WHERE path = $path;";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(imagePath));
        return command.ExecuteNonQuery();
    }

    public int CountImages()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM images;";
        var result = command.ExecuteScalar();
        return Convert.ToInt32(result);
    }

    public bool ImagesTableExists()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'images';";
        var result = command.ExecuteScalar();
        return Convert.ToInt32(result) > 0;
    }

    private void EnsureColumnExists(string columnName, string columnDefinition)
    {
        using var existsCommand = _connection.CreateCommand();
        existsCommand.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('images') WHERE name = $name;";
        existsCommand.Parameters.AddWithValue("$name", columnName);

        var exists = Convert.ToInt32(existsCommand.ExecuteScalar()) > 0;
        if (exists)
        {
            return;
        }

        using var alterCommand = _connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE images ADD COLUMN {columnName} {columnDefinition};";
        alterCommand.ExecuteNonQuery();
    }

    private static string? DbString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? DbInt(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static double? DbDouble(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static DateTimeOffset? DbDateTimeOffset(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return ParseUtc(reader.GetString(ordinal));
    }

    private static DateTimeOffset ParseUtc(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;

    private static object DbValue<T>(T? value)
        where T : struct
        => value.HasValue ? value.Value : DBNull.Value;

    public void Dispose()
    {
        _connection.Dispose();
    }
}
