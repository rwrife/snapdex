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
                indexed_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void UpsertImage(ScannedImageFile image)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO images (path, filename, size, mtime, indexed_at)
            VALUES ($path, $filename, $size, $mtime, $indexedAt)
            ON CONFLICT(path) DO UPDATE SET
                filename = excluded.filename,
                size = excluded.size,
                mtime = excluded.mtime,
                indexed_at = excluded.indexed_at;
            """;

        command.Parameters.AddWithValue("$path", image.Path);
        command.Parameters.AddWithValue("$filename", image.Filename);
        command.Parameters.AddWithValue("$size", image.Size);
        command.Parameters.AddWithValue("$mtime", image.ModifiedTimeUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$indexedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));

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

    public void Dispose()
    {
        _connection.Dispose();
    }
}
