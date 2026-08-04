namespace SnapdexCore.Indexing;

public sealed record CachedImageEmbedding(
    string Path,
    string Model,
    long SourceSize,
    DateTimeOffset SourceModifiedTimeUtc,
    float[] Vector,
    DateTimeOffset IndexedAtUtc);
