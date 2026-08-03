namespace SnapdexCore.LocalAi;

public sealed record LocalAiHealthStatus(bool IsHealthy, string Message)
{
    public static LocalAiHealthStatus Healthy(string message = "Local-AI endpoint reachable.") => new(true, message);

    public static LocalAiHealthStatus Unhealthy(string message) => new(false, message);
}
