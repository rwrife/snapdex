using SnapdexCore.Indexing;
using SnapdexCore.LocalAi;
using SnapdexCore.Search;

namespace SnapdexCore.Tests;

public class VisualSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_VisualTextQuery_RanksByCosineAndCachesEmbeddings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"snapdex-visual-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(Path.GetTempPath(), $"snapdex-visual-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(root);

        var firstPath = Path.Combine(root, "first.jpg");
        var secondPath = Path.Combine(root, "second.jpg");
        await File.WriteAllBytesAsync(firstPath, new byte[] { 1, 2, 3, 4 });
        await File.WriteAllBytesAsync(secondPath, new byte[] { 5, 6, 7, 8 });

        var firstInfo = new FileInfo(firstPath);
        var secondInfo = new FileInfo(secondPath);

        try
        {
            using var index = new SqliteImageIndex(dbPath);
            index.EnsureCreated();

            index.UpsertImage(new ScannedImageFile(
                firstInfo.FullName,
                firstInfo.Name,
                firstInfo.Length,
                firstInfo.LastWriteTimeUtc,
                CameraMake: "Canon"));

            index.UpsertImage(new ScannedImageFile(
                secondInfo.FullName,
                secondInfo.Name,
                secondInfo.Length,
                secondInfo.LastWriteTimeUtc,
                CameraMake: "Canon"));

            var parser = new SearchQueryParser();
            var translator = new SqliteQueryTranslator();
            var parse = parser.Parse("~ \"sunset\" camera:Canon");
            Assert.True(parse.Success, parse.Error);

            var translation = translator.Translate(parse.Query!);

            var fake = new FakeEmbeddingClient
            {
                Health = LocalAiHealthStatus.Healthy(),
                TextVectors =
                {
                    ["sunset"] = new[] { 1f, 0f }
                },
                ImageVectors =
                {
                    [firstInfo.FullName] = new[] { 0.9f, 0.1f },
                    [secondInfo.FullName] = new[] { 0.1f, 0.9f }
                }
            };

            var service = new VisualSearchService(fake);
            var result = await service.SearchAsync(index, translation, new LocalAiSettings("http://127.0.0.1:11434", "test-model"));

            Assert.True(result.UsedVisualRanking, result.Notice);
            Assert.Equal(2, result.Records.Count);
            Assert.Equal(firstInfo.FullName, result.Records[0].Path);
            Assert.Equal(secondInfo.FullName, result.Records[1].Path);

            var firstCached = index.GetImageEmbedding(firstInfo.FullName, "test-model");
            var secondCached = index.GetImageEmbedding(secondInfo.FullName, "test-model");

            Assert.NotNull(firstCached);
            Assert.NotNull(secondCached);
            Assert.Equal(firstInfo.Length, firstCached!.SourceSize);
            Assert.Equal(secondInfo.Length, secondCached!.SourceSize);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task SearchAsync_WhenHealthCheckFails_FallsBackToMetadataOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"snapdex-visual-fallback-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(Path.GetTempPath(), $"snapdex-visual-fallback-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(root);

        var imagePath = Path.Combine(root, "only.jpg");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 1, 2, 3, 4 });

        var info = new FileInfo(imagePath);

        try
        {
            using var index = new SqliteImageIndex(dbPath);
            index.EnsureCreated();
            index.UpsertImage(new ScannedImageFile(info.FullName, info.Name, info.Length, info.LastWriteTimeUtc));

            var parser = new SearchQueryParser();
            var translator = new SqliteQueryTranslator();
            var parse = parser.Parse("~ \"whiteboard\"");
            Assert.True(parse.Success, parse.Error);

            var translation = translator.Translate(parse.Query!);

            var fake = new FakeEmbeddingClient
            {
                Health = LocalAiHealthStatus.Unhealthy("connection refused")
            };

            var service = new VisualSearchService(fake);
            var result = await service.SearchAsync(index, translation, new LocalAiSettings("http://127.0.0.1:11434", "test-model"));

            Assert.False(result.UsedVisualRanking);
            Assert.Single(result.Records);
            Assert.Contains("metadata-only", result.Notice, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private sealed class FakeEmbeddingClient : ILocalAiEmbeddingClient
    {
        public LocalAiHealthStatus Health { get; init; } = LocalAiHealthStatus.Healthy();

        public Dictionary<string, float[]> TextVectors { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, float[]> ImageVectors { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<LocalAiHealthStatus> CheckHealthAsync(LocalAiSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(Health);

        public Task<float[]?> TryEmbedTextAsync(LocalAiSettings settings, string text, CancellationToken cancellationToken = default)
        {
            TextVectors.TryGetValue(text, out var vector);
            return Task.FromResult(vector);
        }

        public Task<float[]?> TryEmbedImageAsync(LocalAiSettings settings, string imagePath, CancellationToken cancellationToken = default)
        {
            var fullPath = Path.GetFullPath(imagePath);
            ImageVectors.TryGetValue(fullPath, out var vector);
            return Task.FromResult(vector);
        }
    }
}
