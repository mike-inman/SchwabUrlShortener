using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Shortener.Web.Components.Pages;
using Xunit;

namespace Shortener.WebTests;

public sealed class AppSettingsStartupTests
{
    [Fact]
    public async Task Web_starts_with_data_protection_and_token_cache_defaults_from_appsettings()
    {
        using var factory = new WebFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        AssertAppSettingsValue(configuration, "DataProtection:Mode", "Filesystem");
        AssertAppSettingsValue(configuration, "DataProtection:ApplicationName", "Shortener.Web");
        AssertAppSettingsValue(configuration, "TokenCache:Mode", "InMemory");
    }

    private static void AssertAppSettingsValue(IConfiguration configuration, string key, string expected)
    {
        Assert.Equal(expected, configuration[key]);
        var provider = ((IConfigurationRoot)configuration).Providers.Reverse()
            .First(source => source.TryGet(key, out _));
        Assert.Equal("appsettings.json", Assert.IsType<JsonConfigurationProvider>(provider).Source.Path);
    }

    private sealed class WebFactory : WebApplicationFactory<SignedOut>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("LiveUpdates:Mode", "Redis");
            builder.UseSetting("LiveUpdates:Channel", "shortener:links:v1");
            builder.UseSetting("LiveUpdates:DebounceMilliseconds", "1000");
            builder.UseSetting("ConnectionStrings:Redis", Environment.GetEnvironmentVariable("ConnectionStrings__Redis") ?? "localhost:6379");
        }
    }
}
