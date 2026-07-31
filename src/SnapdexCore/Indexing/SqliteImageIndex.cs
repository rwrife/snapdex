using Microsoft.Data.Sqlite;

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

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;

    private static object DbValue<T>(T? value)
        where T : struct
        => value.HasValue ? value.Value : DBNull.Value;

    public void Dispose()
    {
        _connection.Dispose();
    }
}
