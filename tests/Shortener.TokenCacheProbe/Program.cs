using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web.TokenCacheProviders.Distributed;
using Shortener.Shared;
using Shortener.Web.Services;

var mode = args.Single();
var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var services = new ServiceCollection();
services.AddLogging();
ScaleoutHostConfiguration.AddConfiguredDataProtection(services, config);
if (!TokenCacheConfiguration.AddConfiguredTokenCache(services, config)) throw new Exception("Distributed mode required");
services.AddSingleton<MsalDistributedTokenCacheAdapter>();
using var provider = services.BuildServiceProvider();
var adapter = provider.GetRequiredService<MsalDistributedTokenCacheAdapter>();
var client = ConfidentialClientApplicationBuilder
    .Create("11111111-1111-1111-1111-111111111111")
    .WithClientSecret("synthetic-probe-secret")
    .WithAuthority("https://login.microsoftonline.com/22222222-2222-2222-2222-222222222222")
    .WithHttpClientFactory(new ProbeHttpFactory(mode))
    .Build();
await adapter.InitializeAsync(client.AppTokenCache);
var result = await client.AcquireTokenForClient(["api://tracka-cache-probe/.default"]).ExecuteAsync();
if (result.AccessToken != "synthetic-access-token") throw new Exception("Unexpected cached token");
Console.WriteLine(mode == "write" ? "msal-cache-write=pass" : "msal-cache-cross-replica-read=pass");

sealed class ProbeHttpFactory(string mode) : IMsalHttpClientFactory
{
    public HttpClient GetHttpClient() => new(new ProbeHandler(mode));
}

sealed class ProbeHandler(string mode) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (mode == "read") throw new Exception("Cache miss required a network token request");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"synthetic-access-token\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"ext_expires_in\":3600}", Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
