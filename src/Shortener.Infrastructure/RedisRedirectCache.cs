using System.Text.Json;
using Shortener.Application.Interfaces;
using Shortener.Domain;
using StackExchange.Redis;

namespace Shortener.Infrastructure;

public sealed class RedisRedirectCache(string? connectionString, string? keyNamespace = null) : IRedirectCache, IAsyncDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMilliseconds(750);
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private ConnectionMultiplexer? _connection;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string KeyFor(string shortCode, string? keyNamespace = null)
    {
        if (!UrlValidation.IsValidShortCode(shortCode))
            throw new ArgumentException("A valid short code is required.", nameof(shortCode));
        var prefix = string.IsNullOrWhiteSpace(keyNamespace) ? "shortener:redirect:v1" : keyNamespace;
        if (prefix.Length > 80 || prefix.Any(c => c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or ':' or '-')))
            throw new ArgumentException("Redirect cache namespace must contain only ASCII letters, digits, colons, or hyphens.", nameof(keyNamespace));
        return prefix + ":" + shortCode.Trim().ToLowerInvariant();
    }

    public async Task<RedirectTarget?> GetAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var db = (await GetConnectionAsync(cancellationToken)).GetDatabase();
        var value = await db.StringGetAsync(KeyFor(shortCode, keyNamespace)).WaitAsync(OperationTimeout, cancellationToken);
        return value.IsNull ? null : JsonSerializer.Deserialize<RedirectTarget>((string)value!, JsonOptions);
    }

    public async Task SetAsync(RedirectTarget target, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero) return;
        var db = (await GetConnectionAsync(cancellationToken)).GetDatabase();
        var value = JsonSerializer.Serialize(target, JsonOptions);
        await db.StringSetAsync(KeyFor(target.ShortCode, keyNamespace), value, ttl).WaitAsync(OperationTimeout, cancellationToken);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = KeyFor("health", keyNamespace);
            var db = (await GetConnectionAsync(cancellationToken)).GetDatabase();
            await db.PingAsync().WaitAsync(OperationTimeout, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }
    }

    private async Task<ConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { } existing) return existing;
        if (!await _connectGate.WaitAsync(OperationTimeout, cancellationToken))
            throw new TimeoutException("Redis connection wait exceeded the redirect cache limit.");
        try
        {
            if (_connection is { } connected) return connected;
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings:Redis must be configured for redirect caching.");
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 0;
            options.ConnectTimeout = 500;
            options.AsyncTimeout = 500;
            options.BacklogPolicy = BacklogPolicy.FailFast;
            _connection = await ConnectionMultiplexer.ConnectAsync(options).WaitAsync(OperationTimeout, cancellationToken);
            return _connection;
        }
        finally { _connectGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is { } connection) await connection.DisposeAsync();
        _connectGate.Dispose();
    }
}
