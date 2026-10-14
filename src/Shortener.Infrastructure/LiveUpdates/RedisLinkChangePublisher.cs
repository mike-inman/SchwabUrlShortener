using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shortener.Application;
using StackExchange.Redis;

namespace Shortener.Infrastructure.LiveUpdates;

public sealed class RedisLinkChangePublisher(
    IServiceProvider services, IConfiguration configuration,
    ILogger<RedisLinkChangePublisher> logger) : ILinkChangePublisher
{
    public async Task PublishAsync(LinkChange change, CancellationToken cancellationToken = default)
    {
        using var activity = RedisTelemetry.Source.StartActivity("redis.live.publish", System.Diagnostics.ActivityKind.Client);
        try
        {
            await services.GetRequiredService<IConnectionMultiplexer>().GetSubscriber().PublishAsync(
                RedisChannel.Literal(configuration["LiveUpdates:Channel"]!), change.Serialize())
                .WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            LiveUpdateMetrics.Published.Add(1, new KeyValuePair<string, object?>("scope", change.Scope.ToString().ToLowerInvariant()));
        }
        catch (Exception ex)
        {
            LiveUpdateMetrics.PublishFailed.Add(1, new KeyValuePair<string, object?>("scope", change.Scope.ToString().ToLowerInvariant()));
            logger.LogWarning("Live update publish failed. Scope={Scope} ExceptionType={ExceptionType} TraceId={TraceId}",
                change.Scope, ex.GetType().Name, System.Diagnostics.Activity.Current?.Id);
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
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            options.BacklogPolicy = BacklogPolicy.FailFast;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<ILinkChangePublisher, RedisLinkChangePublisher>();
        return services;
    }
}
