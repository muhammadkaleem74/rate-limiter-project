using Microsoft.Extensions.Options;
using RateLimiterApi.Configuration;
using System.Collections.Concurrent;

namespace RateLimiterApi.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitConfig _config;
    private readonly ConcurrentDictionary<string, WindowTracker> _windows = new();

    public RateLimitingMiddleware(RequestDelegate next, IOptions<RateLimitConfig> config)
    {
        _next = next;
        _config = config.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = context.Request.Headers["X-Client-Id"].FirstOrDefault() ?? "anonymous";
        var clientTier = context.Request.Headers["X-Client-Tier"].FirstOrDefault()?.ToLower() ?? "standard";
        var method = context.Request.Method.ToUpper();
        var path = context.Request.Path.Value?.ToLower() ?? "/";

        var clientLimit = _config.Clients.TryGetValue(clientTier, out var cl) ? cl : _config.DefaultLimit;

        // Match endpoint config by prefix so "/api/data/42" matches key "DELETE:/api/data"
        var endpointKey = ResolveEndpointKey(method, path);
        var endpointLimit = endpointKey is not null && _config.Endpoints.TryGetValue(endpointKey, out var el)
            ? (int?)el : null;

        // Retrieve (or create) trackers — use the resolved key for the endpoint tracker
        var clientTracker = _windows.GetOrAdd($"client:{clientId}", _ => new WindowTracker());
        WindowTracker? epTracker = endpointLimit.HasValue
            ? _windows.GetOrAdd($"ep:{clientId}:{endpointKey}", _ => new WindowTracker())
            : null;

        // Endpoint limit is checked first — it is typically more restrictive
        if (epTracker is not null && epTracker.IsAtLimit(endpointLimit!.Value, _config.WindowSeconds))
        {
            await WriteRateLimitResponse(context, endpointLimit.Value, epTracker, _config.WindowSeconds,
                $"Endpoint limit exceeded for {method} {path}");
            return;
        }

        if (clientTracker.IsAtLimit(clientLimit, _config.WindowSeconds))
        {
            await WriteRateLimitResponse(context, clientLimit, clientTracker, _config.WindowSeconds,
                $"Client limit exceeded for tier '{clientTier}'");
            return;
        }

        // Both checks passed — record the request
        epTracker?.Record();
        clientTracker.Record();

        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-RateLimit-Limit"] = clientLimit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = clientTracker.GetRemaining(clientLimit).ToString();
            context.Response.Headers["X-RateLimit-Reset"] = clientTracker.GetResetEpoch(_config.WindowSeconds).ToString();
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private string? ResolveEndpointKey(string method, string path)
    {
        // Exact match first
        var exact = $"{method}:{path}";
        if (_config.Endpoints.ContainsKey(exact)) return exact;

        // Prefix match — allows "DELETE:/api/data" to cover "DELETE:/api/data/42"
        foreach (var key in _config.Endpoints.Keys)
        {
            if (key.StartsWith($"{method}:", StringComparison.OrdinalIgnoreCase))
            {
                var configPath = key[(method.Length + 1)..];
                if (path.StartsWith(configPath, StringComparison.OrdinalIgnoreCase))
                    return key;
            }
        }
        return null;
    }

    private static async Task WriteRateLimitResponse(
        HttpContext context, int limit, WindowTracker tracker, int windowSeconds, string detail)
    {
        var retryAfter = tracker.GetRetryAfterSeconds(windowSeconds);

        context.Response.StatusCode = 429;
        context.Response.ContentType = "application/json";
        context.Response.Headers["Retry-After"] = retryAfter.ToString();
        context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = "0";
        context.Response.Headers["X-RateLimit-Reset"] = tracker.GetResetEpoch(windowSeconds).ToString();

        await context.Response.WriteAsJsonAsync(new
        {
            error = "Too Many Requests",
            detail,
            retryAfterSeconds = retryAfter,
            limit
        });
    }
}

/// <summary>
/// Thread-safe sliding-window counter backed by an in-memory timestamp queue.
/// Known limitation: counter resets on app restart (in-memory by design).
/// Does not synchronise across multiple instances — single-node use only.
/// </summary>
internal sealed class WindowTracker
{
    private readonly object _lock = new();
    private readonly Queue<long> _timestamps = new();

    public bool IsAtLimit(int limit, int windowSeconds)
    {
        lock (_lock)
        {
            Purge(windowSeconds);
            return _timestamps.Count >= limit;
        }
    }

    public void Record()
    {
        lock (_lock)
        {
            _timestamps.Enqueue(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }

    public int GetRemaining(int limit)
    {
        lock (_lock)
        {
            return Math.Max(0, limit - _timestamps.Count);
        }
    }

    public long GetResetEpoch(int windowSeconds)
    {
        lock (_lock)
        {
            if (_timestamps.Count == 0)
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + windowSeconds;
            return (_timestamps.Peek() / 1000L) + windowSeconds;
        }
    }

    public int GetRetryAfterSeconds(int windowSeconds)
    {
        lock (_lock)
        {
            if (_timestamps.Count == 0) return 1;
            var oldestMs = _timestamps.Peek();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var msUntilFree = (oldestMs + (windowSeconds * 1000L)) - nowMs;
            return (int)Math.Max(1, Math.Ceiling(msUntilFree / 1000.0));
        }
    }

    private void Purge(int windowSeconds)
    {
        var cutoffMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (windowSeconds * 1000L);
        while (_timestamps.Count > 0 && _timestamps.Peek() < cutoffMs)
            _timestamps.Dequeue();
    }
}
