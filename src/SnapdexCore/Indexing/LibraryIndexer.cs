namespace SnapdexCore.Indexing;

public sealed class LibraryIndexer
{
    private readonly LibraryScanner _scanner;
    private readonly string _databasePath;

    public LibraryIndexer(string databasePath, LibraryScanner? scanner = null)
    {
        _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        _scanner = scanner ?? new LibraryScanner();
    }

    public int IndexFolder(string rootPath, Action<ScannedImageFile>? onImageIndexed = null)
    {
        using var index = new SqliteImageIndex(_databasePath);
        index.EnsureCreated();

        var indexed = 0;
        foreach (var image in _scanner.Scan(rootPath))
        {
            index.UpsertImage(image);
            onImageIndexed?.Invoke(image);
            indexed++;
        }

        return indexed;
    }
}
