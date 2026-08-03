namespace SnapdexCore.LocalAi;

public sealed record LocalAiSettings(string EndpointUrl, string Model)
{
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(EndpointUrl) && !string.IsNullOrWhiteSpace(Model);

    public LocalAiSettings Normalize()
    {
        var endpoint = (EndpointUrl ?? string.Empty).Trim();
        var model = (Model ?? string.Empty).Trim();

        if (endpoint.EndsWith('/'))
        {
            endpoint = endpoint.TrimEnd('/');
        }

        return this with { EndpointUrl = endpoint, Model = model };
    }

    public static LocalAiSettings Default
        => new("http://127.0.0.1:11434", "nomic-embed-text");
}
