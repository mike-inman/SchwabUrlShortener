using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shortener.IntegrationTests;

public class ShortenerApiTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task GuestIsAnonymousRedirectOnlyAndExpiresAtSevenDays()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var create = await client.PostAsJsonAsync("/api/urls/guest", new { originalUrl = "example.com", shortCode = "guest123" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var body = await create.Content.ReadFromJsonAsync<CreateUrlResponse>();
        Assert.NotNull(body);
        Assert.Equal(factory.Clock.GetUtcNow().AddDays(7), body.ExpiresAtUtc);
        Assert.Equal("/r/guest123", body.ShortUrl);
        var redirect = await client.GetAsync(body.ShortUrl);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https://example.com", redirect.Headers.Location?.ToString().TrimEnd('/'));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/urls/managed/guest123/analytics")).StatusCode);
        using var managedClient = ClientFor(factory, TenantA);
        Assert.Equal(HttpStatusCode.NotFound, (await managedClient.GetAsync("/api/urls/managed/guest123")).StatusCode);
        factory.Clock.Advance(TimeSpan.FromDays(7));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(body.ShortUrl)).StatusCode);
    }

    [Fact]
    public async Task ManagedOperationsAreTenantScopedAndCodesGloballyUnique()
    {
        using var factory = new TestFactory();
        using var a = ClientFor(factory, TenantA);
        using var b = ClientFor(factory, TenantB);
        using var anon = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com" })).StatusCode);
        var created = await a.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com", shortCode = "manag123", tenantId = TenantB });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/urls/managed/manag123")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/urls/managed/manag123/analytics")).StatusCode);
        Assert.Empty((await b.GetFromJsonAsync<ManagedUrlResponse[]>("/api/urls/managed/analytics"))!);
        Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync("/api/urls/guest", new { originalUrl = "other.example", shortCode = "manag123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await anon.GetAsync("/r/manag123")).StatusCode);
        var analytics = await a.GetFromJsonAsync<ManagedUrlResponse>("/api/urls/managed/manag123/analytics");
        Assert.Equal(1, analytics!.HitCount);

        Assert.Equal(HttpStatusCode.Created, (await b.PostAsJsonAsync("/api/urls/managed", new { originalUrl = "example.com", shortCode = "manag456" })).StatusCode);
    }

    [Fact]
    public async Task InvalidTenantContextIsForbidden()
    {
        using var factory = new TestFactory();
        using var client = ClientFor(factory, Guid.Empty);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/urls/managed/analytics")).StatusCode);
    }

    private static HttpClient ClientFor(TestFactory factory, Guid tenant)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant.ToString());
        return client;
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        public AdjustableClock Clock { get; } = new();
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:ClientId"] = "11111111-1111-1111-1111-111111111111"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultForbidScheme = "Test";
                });
            });
        }
    }

    private sealed class AdjustableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    // Test-only authentication replaces bearer validation inside WebApplicationFactory.
    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Tenant", out var value))
                return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tid", value.ToString()) }, "Test"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }
}
