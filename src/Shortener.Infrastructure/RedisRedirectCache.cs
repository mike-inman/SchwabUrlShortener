using System.Text.Json;
using Shortener.Application.Interfaces;
using Shortener.Domain;
using StackExchange.Redis;

namespace Shortener.Infrastructure;

public sealed class RedisRedirectCache : IRedirectCache, IAsyncDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMilliseconds(750);
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly string? _connectionString;
    private readonly string? _keyNamespace;
    private readonly Func<ConfigurationOptions, Task<ConnectionMultiplexer>> _connectAsync;
    private ConnectionMultiplexer? _connection;
    private Task<ConnectionMultiplexer>? _connecting;
    private bool _disposed;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RedisRedirectCache(string? connectionString, string? keyNamespace = null)
        : this(connectionString, keyNamespace, options => ConnectionMultiplexer.ConnectAsync(options)) { }

    internal RedisRedirectCache(string? connectionString, string? keyNamespace,
        Func<ConfigurationOptions, Task<ConnectionMultiplexer>> connectAsync)
    {
        _connectionString = connectionString;
        _keyNamespace = keyNamespace;
        _connectAsync = connectAsync;
    }

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
        using var activity = RedisTelemetry.Source.StartActivity("redis.redirect.get", System.Diagnostics.ActivityKind.Client);
        var db = (await GetConnectionAsync(cancellationToken)).GetDatabase();
        var value = await db.StringGetAsync(KeyFor(shortCode, _keyNamespace)).WaitAsync(OperationTimeout, cancellationToken);
        return value.IsNull ? null : JsonSerializer.Deserialize<RedirectTarget>((string)value!, JsonOptions);
    }

    public async Task SetAsync(RedirectTarget target, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero) return;
        using var activity = RedisTelemetry.Source.StartActivity("redis.redirect.set", System.Diagnostics.ActivityKind.Client);
        var db = (await GetConnectionAsync(cancellationToken)).GetDatabase();
        var value = JsonSerializer.Serialize(target, JsonOptions);
        await db.StringSetAsync(KeyFor(target.ShortCode, _keyNamespace), value, ttl).WaitAsync(OperationTimeout, cancellationToken);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        using var activity = RedisTelemetry.Source.StartActivity("redis.ping", System.Diagnostics.ActivityKind.Client);
        try
        {
            _ = KeyFor("health", _keyNamespace);
            var db = (await GetConnectionAsync(cancellationToken)).GetDatabase();
            await db.PingAsync().WaitAsync(OperationTimeout, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }
    }

    private async Task<ConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connection is { } existing) return existing;
        if (!await _connectGate.WaitAsync(OperationTimeout, cancellationToken))
            throw new TimeoutException("Redis connection wait exceeded the redirect cache limit.");
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is { } connected) return connected;
            if (string.IsNullOrWhiteSpace(_connectionString))
                throw new InvalidOperationException("ConnectionStrings:Redis must be configured for redirect caching.");
            if (_connecting is null)
            {
                var options = ConfigurationOptions.Parse(_connectionString);
                options.AbortOnConnectFail = false;
                options.ConnectRetry = 0;
                options.ConnectTimeout = 500;
                options.AsyncTimeout = 500;
                options.BacklogPolicy = BacklogPolicy.FailFast;
                _connecting = _connectAsync(options);
            }
            var pending = _connecting ?? throw new InvalidOperationException("Redis connection attempt was not started.");
            try
            {
                _connection = await pending.WaitAsync(OperationTimeout, cancellationToken);
                _connecting = null;
                return _connection;
            }
            catch
            {
                // A timed-out wait leaves the connection attempt running. Keep it for the next caller.
                // Only a completed failure may be retried with a new attempt.
                if (pending.IsFaulted || pending.IsCanceled) _connecting = null;
                throw;
            }
        }
        finally { _connectGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _connectGate.WaitAsync();
        try
        {
            _disposed = true;
            if (_connection is { } connection) await connection.DisposeAsync();
            else if (_connecting is { } pending) _ = DisposeLateConnectionAsync(pending);
        }
        finally
        {
            _connectGate.Release();
            _connectGate.Dispose();
        }
    }

    private static async Task DisposeLateConnectionAsync(Task<ConnectionMultiplexer> pending)
    {
        try { await (await pending).DisposeAsync(); }
        catch { /* A failed connection has no resource to release. */ }
    }
}
