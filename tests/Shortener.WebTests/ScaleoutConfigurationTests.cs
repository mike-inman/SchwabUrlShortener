using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web.TokenCacheProviders.Distributed;
using Shortener.Shared;
using Shortener.Web.Services;
using StackExchange.Redis;
using Xunit;

namespace Shortener.WebTests;

public class ScaleoutConfigurationTests
{
    [Theory]
    [InlineData("Filesystem")]
    [InlineData("AzureBlob")]
    public void DataProtectionValidModes(string mode)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:Mode"] = mode,
            ["DataProtection:ApplicationName"] = "Shortener.Web",
            ["DataProtection:BlobUri"] = "https://example.blob.core.windows.net/keys/web.xml",
            ["DataProtection:KeyVaultKeyId"] = "https://example.vault.azure.net/keys/web"
        };
        Assert.Equal(mode, ScaleoutHostConfiguration.AddConfiguredDataProtection(new ServiceCollection(), Config(settings)));
    }

    [Theory]
    [InlineData("AzureBlob", null, "https://example.vault.azure.net/keys/web", "DataProtection:BlobUri")]
    [InlineData("AzureBlob", "https://example.blob.core.windows.net/keys/web.xml", null, "DataProtection:KeyVaultKeyId")]
    [InlineData("Unexpected", null, null, "DataProtection:Mode")]
    public void DataProtectionInvalidModesNameSetting(string mode, string? blob, string? key, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ScaleoutHostConfiguration.AddConfiguredDataProtection(
            new ServiceCollection(), Config(new() { ["DataProtection:Mode"] = mode,
                ["DataProtection:ApplicationName"] = "Shortener.Web", ["DataProtection:BlobUri"] = blob,
                ["DataProtection:KeyVaultKeyId"] = key })));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void MissingDataProtectionModeAndApplicationNameFail()
    {
        Assert.Contains("DataProtection:Mode", Assert.Throws<InvalidOperationException>(() =>
            ScaleoutHostConfiguration.AddConfiguredDataProtection(new ServiceCollection(), Config(new()))).Message);
        Assert.Contains("DataProtection:ApplicationName", Assert.Throws<InvalidOperationException>(() =>
            ScaleoutHostConfiguration.AddConfiguredDataProtection(new ServiceCollection(), Config(new()
            { ["DataProtection:Mode"] = "Filesystem" }))).Message);
    }

    [Theory]
    [InlineData(null, null, "None", 1)]
    [InlineData("localhost", null, "KnownProxyHost", 1)]
    [InlineData(null, "10.60.0.0/23", "KnownNetworks", 2)]
    public void ForwardedHeadersTrustModes(string? host, string? networks, string expected, int expectedLimit)
    {
        var services = new ServiceCollection();
        var mode = ScaleoutHostConfiguration.ConfigureTrustedProxy(services, Config(new()
        {
            ["ForwardedHeaders:KnownProxyHost"] = host,
            ["ForwardedHeaders:KnownNetworks"] = networks,
            ["ForwardedHeaders:ForwardLimit"] = expectedLimit.ToString(),
            ["PublicLinks:BaseUrl"] = "https://example.com/"
        }));
        Assert.Equal(expected, mode);
        if (mode == "None") return;
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(expectedLimit, options.ForwardLimit);
        Assert.Contains("example.com", options.AllowedHosts);
        Assert.Equal(mode == "KnownNetworks" ? 1 : 0, options.KnownIPNetworks.Count);
    }

    [Fact]
    public void ConflictingTrustSettingsFail()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ScaleoutHostConfiguration.ConfigureTrustedProxy(
            new ServiceCollection(), Config(new() { ["ForwardedHeaders:KnownProxyHost"] = "localhost",
                ["ForwardedHeaders:KnownNetworks"] = "10.60.0.0/23" })));
        Assert.Contains("ForwardedHeaders:KnownProxyHost", ex.Message);
        Assert.Contains("ForwardedHeaders:KnownNetworks", ex.Message);
    }

    [Fact]
    public void InvalidCidrAndLimitFail()
    {
        Assert.Contains("ForwardedHeaders:KnownNetworks", Assert.Throws<InvalidOperationException>(() =>
            ScaleoutHostConfiguration.ConfigureTrustedProxy(new ServiceCollection(), Config(new()
            { ["ForwardedHeaders:KnownNetworks"] = "not-a-cidr" }))).Message);
        Assert.Contains("ForwardedHeaders:ForwardLimit", Assert.Throws<InvalidOperationException>(() =>
            ScaleoutHostConfiguration.ConfigureTrustedProxy(new ServiceCollection(), Config(new()
            { ["ForwardedHeaders:ForwardLimit"] = "0" }))).Message);
    }

    [Fact]
    public void TokenCacheModesValidateAndEncrypt()
    {
        Assert.False(TokenCacheConfiguration.AddConfiguredTokenCache(new ServiceCollection(), Config(new()
        { ["TokenCache:Mode"] = "InMemory" })));
        var services = new ServiceCollection();
        Assert.True(TokenCacheConfiguration.AddConfiguredTokenCache(services, Config(new()
        { ["TokenCache:Mode"] = "Distributed", ["TokenCache:InstanceName"] = "shortener:web:tokens:",
            ["ConnectionStrings:Redis"] = "localhost:6379" })));
        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<IOptions<MsalDistributedTokenCacheAdapterOptions>>().Value.Encrypt);
        Assert.False(provider.GetRequiredService<IOptions<MsalDistributedTokenCacheAdapterOptions>>().Value.EnableAsyncL2Write);
        Assert.NotNull(provider.GetRequiredService<IDistributedCache>());
        Assert.Single(services, x => x.ServiceType == typeof(IConnectionMultiplexer));
    }

    [Fact]
    public void TokenCacheInvalidConfigurationNamesSettingsWithoutSecrets()
    {
        foreach (var settings in new[]
        {
            new Dictionary<string, string?>(),
            new Dictionary<string, string?> { ["TokenCache:Mode"] = "Bad" },
            new Dictionary<string, string?> { ["TokenCache:Mode"] = "Distributed" },
            new Dictionary<string, string?> { ["TokenCache:Mode"] = "Distributed", ["ConnectionStrings:Redis"] = "localhost:6379,password=private-value" }
        })
        {
            var ex = Assert.Throws<InvalidOperationException>(() => TokenCacheConfiguration.AddConfiguredTokenCache(
                new ServiceCollection(), Config(settings)));
            Assert.DoesNotContain("private-value", ex.Message);
            Assert.Contains(!settings.TryGetValue("TokenCache:Mode", out var mode) || mode == "Bad" ? "TokenCache:Mode" :
                settings.ContainsKey("ConnectionStrings:Redis") ? "TokenCache:InstanceName" : "ConnectionStrings:Redis", ex.Message);
        }
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
