namespace RateLimiterApi.Configuration;

public class RateLimitConfig
{
    public int WindowSeconds { get; set; } = 60;
    public int DefaultLimit { get; set; } = 100;

    // Key: tier name (e.g. "standard", "premium"), Value: max requests per window
    public Dictionary<string, int> Clients { get; set; } = new();

    // Key: "METHOD:/path" (e.g. "POST:/api/data"), Value: max requests per window
    public Dictionary<string, int> Endpoints { get; set; } = new();
}
