using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SnapdexCore.LocalAi;

public sealed class OpenAiCompatibleEmbeddingClient : ILocalAiEmbeddingClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    public OpenAiCompatibleEmbeddingClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient is null;

        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(8);
        }
    }

    public async Task<LocalAiHealthStatus> CheckHealthAsync(LocalAiSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = settings.Normalize();
        if (!normalized.IsConfigured)
        {
            return LocalAiHealthStatus.Unhealthy("Local-AI settings are incomplete. Configure endpoint URL and model.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(normalized.EndpointUrl, "/v1/models"));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return LocalAiHealthStatus.Healthy($"Endpoint reachable ({(int)response.StatusCode}).");
            }

            return LocalAiHealthStatus.Unhealthy($"Endpoint responded {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (Exception ex)
        {
            return LocalAiHealthStatus.Unhealthy($"Endpoint not reachable: {ex.Message}");
        }
    }

    public Task<float[]?> TryEmbedTextAsync(LocalAiSettings settings, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<float[]?>(null);
        }

        var payload = new
        {
            model = settings.Model,
            input = text.Trim()
        };

        return TryEmbedRequestAsync(settings, payload, cancellationToken);
    }

    public async Task<float[]?> TryEmbedImageAsync(LocalAiSettings settings, string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        }
        catch
        {
            return null;
        }

        var dataUrl = BuildImageDataUrl(imagePath, bytes);

        var candidates = new object[]
        {
            new
            {
                model = settings.Model,
                input = new object[]
                {
                    new
                    {
                        type = "input_image",
                        image_url = dataUrl
                    }
                }
            },
            new
            {
                model = settings.Model,
                input = dataUrl
            },
            new
            {
                model = settings.Model,
                input = $"image_base64:{Convert.ToBase64String(bytes)}"
            }
        };

        foreach (var payload in candidates)
        {
            var embedding = await TryEmbedRequestAsync(settings, payload, cancellationToken);
            if (embedding is { Length: > 0 })
            {
                return embedding;
            }
        }

        return null;
    }

    private async Task<float[]?> TryEmbedRequestAsync(LocalAiSettings settings, object payload, CancellationToken cancellationToken)
    {
        var normalized = settings.Normalize();
        if (!normalized.IsConfigured)
        {
            return null;
        }

        try
        {
            var uri = BuildUri(normalized.EndpointUrl, "/v1/embeddings");
            var content = JsonSerializer.Serialize(payload, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return TryReadEmbedding(document.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static float[]? TryReadEmbedding(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0)
        {
            return null;
        }

        var first = data[0];
        if (!first.TryGetProperty("embedding", out var embeddingJson)
            || embeddingJson.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var vector = new float[embeddingJson.GetArrayLength()];
        var index = 0;

        foreach (var value in embeddingJson.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number))
            {
                return null;
            }

            vector[index++] = number;
        }

        return vector;
    }

    private static Uri BuildUri(string endpointUrl, string path)
    {
        var normalized = endpointUrl.Trim();
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "http://" + normalized;
        }

        normalized = normalized.TrimEnd('/');
        return new Uri($"{normalized}{path}", UriKind.Absolute);
    }

    private static string BuildImageDataUrl(string path, byte[] bytes)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var mimeType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".tif" or ".tiff" => "image/tiff",
            ".heic" or ".heif" => "image/heic",
            _ => "application/octet-stream"
        };

        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
