using SnapdexCore.Indexing;
using SnapdexCore.LocalAi;

namespace SnapdexCore.Search;

public sealed record VisualSearchResult(
    IReadOnlyList<IndexedImageRecord> Records,
    bool UsedVisualRanking,
    string Notice);

public sealed class VisualSearchService
{
    private readonly ILocalAiEmbeddingClient _embeddingClient;

    public VisualSearchService(ILocalAiEmbeddingClient embeddingClient)
    {
        _embeddingClient = embeddingClient ?? throw new ArgumentNullException(nameof(embeddingClient));
    }

    public async Task<VisualSearchResult> SearchAsync(
        SqliteImageIndex index,
        SqliteQueryTranslation translation,
        LocalAiSettings? settings,
        int limit = 20000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(translation);

        var metadataResults = index.Search(translation, limit);
        if (!translation.IsVisualQuery)
        {
            return new VisualSearchResult(metadataResults, false, string.Empty);
        }

        if (settings is null || !settings.Normalize().IsConfigured)
        {
            return new VisualSearchResult(
                metadataResults,
                false,
                "Local-AI is not configured. Showing metadata-only results.");
        }

        var normalizedSettings = settings.Normalize();
        var health = await _embeddingClient.CheckHealthAsync(normalizedSettings, cancellationToken);
        if (!health.IsHealthy)
        {
            return new VisualSearchResult(
                metadataResults,
                false,
                $"Local-AI unavailable ({health.Message}). Showing metadata-only results.");
        }

        var queryVector = await ResolveQueryEmbeddingAsync(
            index,
            translation,
            normalizedSettings,
            cancellationToken);

        if (queryVector is not { Length: > 0 })
        {
            return new VisualSearchResult(
                metadataResults,
                false,
                "Could not compute a visual query embedding. Showing metadata-only results.");
        }

        var embeddingsByPath = new Dictionary<string, CachedImageEmbedding>(
            index.GetImageEmbeddingsByModel(normalizedSettings.Model),
            StringComparer.OrdinalIgnoreCase);

        var scored = new List<(IndexedImageRecord Record, double Score)>();
        var newlyCached = 0;

        foreach (var record in metadataResults)
        {
            if (!embeddingsByPath.TryGetValue(record.Path, out var embedding)
                || !IsCurrent(embedding, record.Size, record.ModifiedTimeUtc))
            {
                var newVector = await _embeddingClient.TryEmbedImageAsync(
                    normalizedSettings,
                    record.Path,
                    cancellationToken);

                if (newVector is { Length: > 0 })
                {
                    index.UpsertImageEmbedding(record.Path, normalizedSettings.Model, record.Size, record.ModifiedTimeUtc, newVector);
                    embedding = new CachedImageEmbedding(
                        record.Path,
                        normalizedSettings.Model,
                        record.Size,
                        record.ModifiedTimeUtc,
                        newVector,
                        DateTimeOffset.UtcNow);
                    embeddingsByPath[record.Path] = embedding;
                    newlyCached++;
                }
            }

            if (embedding is null || embedding.Vector.Length == 0)
            {
                continue;
            }

            if (!CanCompare(queryVector, embedding.Vector))
            {
                continue;
            }

            var score = CosineSimilarity(queryVector, embedding.Vector);
            scored.Add((record, score));
        }

        if (scored.Count == 0)
        {
            return new VisualSearchResult(
                metadataResults,
                false,
                "No comparable visual embeddings were available. Showing metadata-only results.");
        }

        var ranked = scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Record.CapturedAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Record.Filename, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Record)
            .ToList();

        var notice = newlyCached > 0
            ? $"Visual ranking enabled ({scored.Count} ranked; cached {newlyCached} new embedding(s))."
            : $"Visual ranking enabled ({scored.Count} ranked).";

        return new VisualSearchResult(ranked, true, notice);
    }

    private async Task<float[]?> ResolveQueryEmbeddingAsync(
        SqliteImageIndex index,
        SqliteQueryTranslation translation,
        LocalAiSettings settings,
        CancellationToken cancellationToken)
    {
        if (translation.VisualQueryKind == VisualQueryKind.Text)
        {
            if (string.IsNullOrWhiteSpace(translation.VisualQueryText))
            {
                return null;
            }

            return await _embeddingClient.TryEmbedTextAsync(settings, translation.VisualQueryText, cancellationToken);
        }

        if (translation.VisualQueryKind == VisualQueryKind.SimilarImage)
        {
            if (string.IsNullOrWhiteSpace(translation.VisualSimilarPath))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(translation.VisualSimilarPath);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var fileInfo = new FileInfo(fullPath);
            var cached = index.GetImageEmbedding(fullPath, settings.Model);
            if (cached is not null && IsCurrent(cached, fileInfo.Length, fileInfo.LastWriteTimeUtc))
            {
                return cached.Vector;
            }

            var vector = await _embeddingClient.TryEmbedImageAsync(settings, fullPath, cancellationToken);
            if (vector is { Length: > 0 })
            {
                index.UpsertImageEmbedding(fullPath, settings.Model, fileInfo.Length, fileInfo.LastWriteTimeUtc, vector);
            }

            return vector;
        }

        return null;
    }

    private static bool IsCurrent(CachedImageEmbedding embedding, long sourceSize, DateTimeOffset sourceModifiedTimeUtc)
        => embedding.SourceSize == sourceSize && embedding.SourceModifiedTimeUtc == sourceModifiedTimeUtc;

    private static bool CanCompare(IReadOnlyList<float> a, IReadOnlyList<float> b)
        => a.Count > 0 && b.Count > 0 && a.Count == b.Count;

    private static double CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        var dot = 0d;
        var normA = 0d;
        var normB = 0d;

        for (var i = 0; i < a.Count; i++)
        {
            var av = a[i];
            var bv = b[i];

            dot += av * bv;
            normA += av * av;
            normB += bv * bv;
        }

        if (normA <= 0 || normB <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
