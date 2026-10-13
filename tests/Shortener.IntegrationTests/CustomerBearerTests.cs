using System.Net;
using System.Net.Http.Headers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Shortener.IntegrationTests;

public sealed class CustomerBearerTests
{
    private const string Tenant = "6adc36e9-dfe5-469f-8aed-6ef83abcdd19";
    private const string Audience = "8911f6b3-365c-4ce5-a0f7-d12b2e4f230a";
    private const string Issuer = "https://" + Tenant + ".ciamlogin.com/" + Tenant + "/v2.0";

    [Fact]
    public async Task Real_bearer_handler_accepts_scoped_access_token_and_rejects_invalid_claims()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__ShortenerDb")
            ?? throw new InvalidOperationException("Guarded PostgreSQL test connection required.");
        var target = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        if (!GuardedTestDatabase.IsAllowed(target))
            throw new InvalidOperationException("Bearer tests require guarded Schwab_test_local.");

        using var key = System.Security.Cryptography.RSA.Create(2048);
        var signingKey = new RsaSecurityKey(key) { KeyId = "customer-test-key" };
        using var factory = new CustomerFactory(connection, signingKey);
        var bearer = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Contains("api://" + Audience, bearer.TokenValidationParameters.ValidAudiences!);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost")
        });

        async Task<HttpStatusCode> StatusAsync(string issuer = Issuer, string audience = Audience,
            string? subject = "person-a", string? scope = "access_as_user", string version = "2.0",
            bool ambiguousSubject = false, bool expired = false)
        {
            var claims = new List<Claim> { new("ver", version), new("tid", Tenant) };
            if (subject is not null) claims.Add(new Claim("sub", subject));
            if (ambiguousSubject) claims.Add(new Claim("sub", "person-b"));
            if (scope is not null) claims.Add(new Claim("scp", scope));
            var token = new JwtSecurityToken(issuer, audience, claims,
                DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(expired ? -6 : 5),
                new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "/api/workspaces/00000000-0000-0000-0000-000000000001/urls/managed/abc");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync());
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(audience: "api://" + Audience));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(issuer: "https://wrong.example/v2.0"));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(audience: "93f2f816-0a95-41e0-b7df-7ca3c0cce8a0"));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(subject: null));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(ambiguousSubject: true));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(scope: null));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(version: "1.0"));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(expired: true));
    }

    private sealed class CustomerFactory(string connection, SecurityKey signingKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DataProtection:Mode", "Filesystem");
            builder.UseSetting("DataProtection:ApplicationName", "Shortener.Api");
            builder.UseSetting("Identity:Mode", "Customer");
            builder.UseSetting("AzureAd:Instance", "https://schwaburlshortener.ciamlogin.com/");
            builder.UseSetting("AzureAd:TenantId", Tenant);
            builder.UseSetting("AzureAd:ClientId", Audience);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Mode"] = "Customer",
                ["DataProtection:Mode"] = "Filesystem",
                ["DataProtection:ApplicationName"] = "Shortener.Api",
                ["AzureAd:Instance"] = "https://schwaburlshortener.ciamlogin.com/",
                ["AzureAd:TenantId"] = Tenant,
                ["AzureAd:ClientId"] = Audience,
                ["ConnectionStrings:ShortenerDb"] = connection,
                ["ConnectionStrings:Redis"] = "localhost:63652,abortConnect=false"
            }));
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                    metadata.SigningKeys.Add(signingKey);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                }));
        }
    }
}
