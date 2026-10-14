using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shortener.Application;
using StackExchange.Redis;

namespace Shortener.Web.Services.LiveUpdates;

public interface ILinkChangeSubscriber
{
    bool IsConnected { get; }
    event Action<bool>? ConnectionChanged;
    IDisposable Subscribe(LinkChangeScope scope, Guid scopeId, Func<Task> onChange);
}

public sealed class RedisLiveUpdates : ILinkChangeSubscriber, IHostedService
{
    private readonly IConnectionMultiplexer connection;
    private readonly ILogger<RedisLiveUpdates> logger;
    private readonly RedisChannel channel;
    private readonly object gate = new();
    private readonly Dictionary<long, (LinkChangeScope Scope, Guid Id, Func<Task> Callback)> callbacks = [];
    private long nextId;
    private bool connected;
    private bool attached;
    private static int connectionState;
    private static readonly ObservableGauge<int> ConnectionGauge = LiveUpdateMetrics.Meter.CreateObservableGauge(
        "shortener.live.connected", () => Volatile.Read(ref connectionState));

    public RedisLiveUpdates(IConnectionMultiplexer connection, IConfiguration configuration, ILogger<RedisLiveUpdates> logger)
    {
        this.connection = connection;
        this.logger = logger;
        channel = RedisChannel.Literal(configuration["LiveUpdates:Channel"]!);
    }

    public bool IsConnected { get { lock (gate) return connected; } }
    public event Action<bool>? ConnectionChanged;

    public IDisposable Subscribe(LinkChangeScope scope, Guid scopeId, Func<Task> onChange)
    {
        if (scopeId == Guid.Empty) throw new ArgumentException("A scope ID is required.", nameof(scopeId));
        long id;
        lock (gate)
        {
            id = ++nextId;
            callbacks.Add(id, (scope, scopeId, onChange));
        }
        return new Subscription(this, id);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        connection.ConnectionFailed += ConnectionFailed;
        connection.ConnectionRestored += ConnectionRestored;
        await AttachAsync();
    }

    private async Task AttachAsync(bool resync = false)
    {
        try
        {
            await connection.GetSubscriber().SubscribeAsync(channel, (receivedChannel, value) =>
            {
                if (LinkChange.TryDeserialize(value.ToString(), out var change)) _ = DispatchAsync(change!);
            });
            attached = true;
            SetConnected(connection.IsConnected, resync);
        }
        catch (Exception ex)
        {
            SetConnected(false, false);
            logger.LogWarning("Live update subscription failed. ExceptionType={ExceptionType}", ex.GetType().Name);
        }
    }

    private void ConnectionFailed(object? sender, ConnectionFailedEventArgs args) => SetConnected(false, false);

    private void ConnectionRestored(object? sender, ConnectionFailedEventArgs args)
    {
        _ = RestoreAsync();
    }

    private async Task RestoreAsync()
    {
        if (!attached) await AttachAsync(true);
        else SetConnected(connection.IsConnected, true);
    }

    private void SetConnected(bool value, bool resync)
    {
        bool changed;
        lock (gate)
        {
            changed = connected != value;
            connected = value;
        }
        Volatile.Write(ref connectionState, value ? 1 : 0);
        if (changed)
        {
            foreach (var handler in ConnectionChanged?.GetInvocationList() ?? [])
            {
                try { ((Action<bool>)handler)(value); }
                catch (Exception ex) { logger.LogWarning("Live update connection callback failed. ExceptionType={ExceptionType}", ex.GetType().Name); }
            }
        }
        if (resync && changed) _ = ResyncAsync();
    }

    private Task ResyncAsync()
    {
        Func<Task>[] snapshot;
        lock (gate) snapshot = callbacks.Values.Select(x => x.Callback).ToArray();
        return DeliverAsync(snapshot, "resync");
    }

    private Task DispatchAsync(LinkChange change)
    {
        LiveUpdateMetrics.Received.Add(1, new KeyValuePair<string, object?>("scope", change.Scope.ToString().ToLowerInvariant()));
        Func<Task>[] snapshot;
        lock (gate) snapshot = callbacks.Values.Where(x => x.Scope == change.Scope && x.Id == change.ScopeId)
            .Select(x => x.Callback).ToArray();
        return DeliverAsync(snapshot, change.Scope.ToString());
    }

    private async Task DeliverAsync(Func<Task>[] snapshot, string scope)
    {
        foreach (var callback in snapshot)
        {
            try
            {
                await callback();
                LiveUpdateMetrics.Dispatched.Add(1, new KeyValuePair<string, object?>("scope", scope));
            }
            catch (Exception ex)
            {
                logger.LogWarning("Live update subscriber failed. Scope={Scope} ExceptionType={ExceptionType}",
                    scope, ex.GetType().Name);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        connection.ConnectionFailed -= ConnectionFailed;
        connection.ConnectionRestored -= ConnectionRestored;
        try { await connection.GetSubscriber().UnsubscribeAsync(channel); }
        catch (Exception ex) { logger.LogWarning("Live update unsubscribe failed. ExceptionType={ExceptionType}", ex.GetType().Name); }
        SetConnected(false, false);
    }

    private sealed class Subscription(RedisLiveUpdates owner, long id) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                lock (owner.gate) owner.callbacks.Remove(id);
        }
    }
}

public static class LiveUpdatesServiceCollectionExtensions
{
    public static IServiceCollection AddLiveUpdates(this IServiceCollection services, IConfiguration configuration)
    {
        if (configuration["LiveUpdates:Mode"] != "Redis")
            throw new InvalidOperationException("LiveUpdates:Mode must be Redis.");
        if (string.IsNullOrWhiteSpace(configuration["LiveUpdates:Channel"]))
            throw new InvalidOperationException("LiveUpdates:Channel must be configured.");
        var connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Redis must be configured for live updates.");
        if (!int.TryParse(configuration["LiveUpdates:DebounceMilliseconds"], out var debounce) || debounce < 1)
            throw new InvalidOperationException("LiveUpdates:DebounceMilliseconds must be a positive integer.");
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            options.BacklogPolicy = BacklogPolicy.FailFast;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<RedisLiveUpdates>();
        services.AddSingleton<ILinkChangeSubscriber>(sp => sp.GetRequiredService<RedisLiveUpdates>());
        services.AddHostedService(sp => sp.GetRequiredService<RedisLiveUpdates>());
        return services;
    }
}
