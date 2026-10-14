using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shortener.Application;
using Shortener.Web.Services.LiveUpdates;
using InfrastructureLiveUpdates = Shortener.Infrastructure.LiveUpdates.LiveUpdatesServiceCollectionExtensions;
using WebLiveUpdates = Shortener.Web.Services.LiveUpdates.LiveUpdatesServiceCollectionExtensions;
using Xunit;

namespace Shortener.WebTests;

public sealed class LiveUpdateStartupDefaultsTests
{
    [Theory]
    [InlineData("Api")]
    [InlineData("Web")]
    [InlineData("Analytics")]
    public void Host_registers_live_updates_with_appsettings_and_a_redis_connection(string host)
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "HostSettings", host, "appsettings.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "127.0.0.1:1,abortConnect=false"
            })
            .Build();
        var services = new ServiceCollection();

        Assert.Equal("Redis", configuration["LiveUpdates:Mode"]);
        Assert.Equal("shortener:links:v1", configuration["LiveUpdates:Channel"]);
        Assert.Equal("1000", configuration["LiveUpdates:DebounceMilliseconds"]);

        if (host == "Web")
        {
            WebLiveUpdates.AddLiveUpdates(services, configuration);
            Assert.Contains(services, service => service.ServiceType == typeof(ILinkChangeSubscriber));
        }
        else
        {
            InfrastructureLiveUpdates.AddLiveUpdates(services, configuration);
            Assert.Contains(services, service => service.ServiceType == typeof(ILinkChangePublisher));
        }
    }
}
