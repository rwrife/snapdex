namespace SnapdexCore.Indexing;

public sealed record IndexedImageState(
    string Path,
    long Size,
    DateTimeOffset ModifiedTimeUtc);
