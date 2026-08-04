namespace SnapdexCore.LocalAi;

public interface ILocalAiEmbeddingClient
{
    Task<LocalAiHealthStatus> CheckHealthAsync(LocalAiSettings settings, CancellationToken cancellationToken = default);

    Task<float[]?> TryEmbedTextAsync(LocalAiSettings settings, string text, CancellationToken cancellationToken = default);

    Task<float[]?> TryEmbedImageAsync(LocalAiSettings settings, string imagePath, CancellationToken cancellationToken = default);
}
