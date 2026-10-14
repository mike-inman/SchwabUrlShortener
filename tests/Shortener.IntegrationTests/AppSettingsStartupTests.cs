using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Shortener.IntegrationTests;

public sealed class AppSettingsStartupTests
{
    [Fact]
    public async Task Api_starts_with_data_protection_defaults_from_appsettings()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        AssertAppSettingsValue(configuration, "DataProtection:Mode", "Filesystem");
        AssertAppSettingsValue(configuration, "DataProtection:ApplicationName", "Shortener.Api");
        AssertAppSettingsValue(configuration, "LiveUpdates:Mode", "Redis");
        AssertAppSettingsValue(configuration, "LiveUpdates:Channel", "shortener:links:v1");
        AssertAppSettingsValue(configuration, "LiveUpdates:DebounceMilliseconds", "1000");
    }

    private static void AssertAppSettingsValue(IConfiguration configuration, string key, string expected)
    {
        Assert.Equal(expected, configuration[key]);
        var provider = ((IConfigurationRoot)configuration).Providers.Reverse()
            .First(source => source.TryGet(key, out _));
        Assert.Equal("appsettings.json", Assert.IsType<JsonConfigurationProvider>(provider).Source.Path);
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("AzureAd:ClientId", "11111111-1111-1111-1111-111111111111");
            builder.UseSetting("ConnectionStrings:ShortenerDb", "Host=localhost;Database=Schwab_test_local;Username=test");
            builder.UseSetting("ConnectionStrings:Redis", "localhost:6379");
            builder.UseSetting("VisitorHash:DailySecret", "integration-test-secret-not-a-real-value");
        }
    }
}
