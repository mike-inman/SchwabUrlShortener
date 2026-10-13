using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shortener.Application;
using Shortener.Infrastructure.LiveUpdates;
using Shortener.Web.Services.LiveUpdates;
using StackExchange.Redis;
using Xunit;
using System.Diagnostics.Metrics;

namespace Shortener.WebTests;

public sealed class LiveUpdateRedisTests
{
    [Fact]
    public async Task Separate_connections_deliver_only_matching_tenant_and_workspace()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Redis")
            ?? throw new InvalidOperationException("Real Redis test connection must be provided in ConnectionStrings__Redis.");
        var channel = "shortener:links:test:" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LiveUpdates:Channel"] = channel
        }).Build();
        await using var publishingConnection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await using var receivingConnection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        using var services = new ServiceCollection().AddSingleton<IConnectionMultiplexer>(publishingConnection).BuildServiceProvider();
        var publisher = new RedisLinkChangePublisher(services, configuration,
            NullLogger<RedisLinkChangePublisher>.Instance);
        var subscriber = new RedisLiveUpdates(receivingConnection, configuration, NullLogger<RedisLiveUpdates>.Instance);
        long receivedMetric = 0;
        using var meterListener = new MeterListener();
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "shortener.live.received") Interlocked.Add(ref receivedMetric, measurement);
        });
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Shortener.LiveUpdates") listener.EnableMeasurementEvents(instrument);
        };
        meterListener.Start();
        await subscriber.StartAsync(default);
        Assert.True(subscriber.IsConnected);
        var tenant = Guid.NewGuid();
        var workspace = tenant; // Equal IDs prove that scope kind also participates in routing.
        var wrongTenant = Guid.NewGuid();
        var tenantReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspaceReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wrongReceived = 0;
        using var failingSubscription = subscriber.Subscribe(LinkChangeScope.Tenant, tenant, () =>
            throw new InvalidOperationException("test callback failure"));
        using var tenantSubscription = subscriber.Subscribe(LinkChangeScope.Tenant, tenant, () =>
        {
            tenantReceived.TrySetResult();
            return Task.CompletedTask;
        });
        using var workspaceSubscription = subscriber.Subscribe(LinkChangeScope.Workspace, workspace, () =>
        {
            workspaceReceived.TrySetResult();
            return Task.CompletedTask;
        });
        using var wrongSubscription = subscriber.Subscribe(LinkChangeScope.Tenant, wrongTenant, () =>
        {
            Interlocked.Increment(ref wrongReceived);
            return Task.CompletedTask;
        });
        await publisher.PublishAsync(new LinkChange(LinkChangeScope.Tenant, tenant, "tenant1"));
        await tenantReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(workspaceReceived.Task.IsCompleted);
        await publisher.PublishAsync(new LinkChange(LinkChangeScope.Workspace, workspace, "team1"));
        await workspaceReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, wrongReceived);
        Assert.True(Volatile.Read(ref receivedMetric) >= 2);
        wrongSubscription.Dispose();
        await publisher.PublishAsync(new LinkChange(LinkChangeScope.Tenant, wrongTenant, "wrong1"));
        await Task.Delay(100);
        Assert.Equal(0, wrongReceived);
        await subscriber.StopAsync(default);
    }
}
