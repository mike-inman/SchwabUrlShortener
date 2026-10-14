using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Identity.Web.TokenCacheProviders.Distributed;
using StackExchange.Redis;

namespace Shortener.Web.Services;

public static class TokenCacheConfiguration
{
    public static bool AddConfiguredTokenCache(IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration["TokenCache:Mode"];
        if (mode is null ||
            (!mode.Equals("InMemory", StringComparison.OrdinalIgnoreCase) &&
             !mode.Equals("Distributed", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("TokenCache:Mode must be InMemory or Distributed.");
        if (mode.Equals("InMemory", StringComparison.OrdinalIgnoreCase)) return false;

        var redis = configuration.GetConnectionString("Redis");
        var instanceName = configuration["TokenCache:InstanceName"];
        if (string.IsNullOrWhiteSpace(redis)) throw new InvalidOperationException("ConnectionStrings:Redis must be configured for TokenCache:Mode Distributed.");
        if (string.IsNullOrWhiteSpace(instanceName)) throw new InvalidOperationException("TokenCache:InstanceName must be configured for TokenCache:Mode Distributed.");
        services.AddStackExchangeRedisCache(options => { options.Configuration = redis; options.InstanceName = instanceName; });
        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
        services.PostConfigure<MsalDistributedTokenCacheAdapterOptions>(options =>
        {
            options.Encrypt = true;
            options.EnableAsyncL2Write = false;
        });
        return true;
    }
}
