namespace SnapdexCore.Indexing;

public sealed record ScannedImageFile(
    string Path,
    string Filename,
    long Size,
    DateTimeOffset ModifiedTimeUtc);
