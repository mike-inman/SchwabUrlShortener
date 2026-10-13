using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shortener.Web.Services;
using Xunit;

namespace Shortener.WebTests;

public sealed class ApiClientTests
{
    private static readonly ClaimsPrincipal User = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "operator")], "test"));
    private const string CreatedJson = """{"shortCode":"abc","originalUrl":"https://example.com","shortUrl":"/r/abc","createdAtUtc":"2026-10-12T00:00:00Z","expiresAtUtc":"2026-10-19T00:00:00Z"}""";
    private const string ManagedJson = """{"shortCode":"abc","originalUrl":"https://example.com","createdAtUtc":"2026-10-12T00:00:00Z","lastAccessedAtUtc":null,"hitCount":3}""";

    [Fact]
    public async Task Guest_creation_uses_anonymous_api_and_public_redirect_origin()
    {
        var calls = 0;
        var client = MakeClient(request =>
        {
            calls++;
            Assert.Equal("/api/urls/guest", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            return Json(HttpStatusCode.Created, CreatedJson);
        });
        var result = await client.CreateGuestAsync(new("https://example.com", null));
        Assert.Equal(1, calls);
        Assert.Equal("https://api.example.test/r/abc", result.PublicUrl.ToString());
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), result.Link.ExpiresAtUtc);
    }

    [Fact]
    public async Task Managed_calls_acquire_bearer_token_and_never_send_tenant_id()
    {
        var paths = new List<string>();
        var client = MakeClient(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            Assert.DoesNotContain("tenant", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
            return request.Method == HttpMethod.Post ? Json(HttpStatusCode.Created, CreatedJson)
                : request.RequestUri.AbsolutePath.EndsWith("/analytics") && !request.RequestUri.AbsolutePath.EndsWith("managed/analytics")
                    ? Json(HttpStatusCode.OK, ManagedJson)
                    : request.RequestUri.AbsolutePath.EndsWith("managed/analytics")
                        ? Json(HttpStatusCode.OK, "[" + ManagedJson + "]")
                        : Json(HttpStatusCode.OK, ManagedJson);
        });
        await client.CreateManagedAsync(new("https://example.com", "abc"), User);
        var links = await client.GetManagedLinksAsync(User);
        await client.GetManagedLinkAsync("abc", User);
        var analytics = await client.GetManagedAnalyticsAsync("abc", User);
        Assert.Single(links);
        Assert.Equal(3, analytics.HitCount);
        Assert.Equal(["/api/urls/managed", "/api/urls/managed/analytics", "/api/urls/managed/abc", "/api/urls/managed/abc/analytics"], paths);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "already in use")]
    [InlineData(HttpStatusCode.Unauthorized, "access was denied")]
    [InlineData(HttpStatusCode.InternalServerError, "Try again later")]
    public async Task Api_failures_have_safe_guidance(HttpStatusCode status, string expected)
    {
        var client = MakeClient(_ => Json(status, "{\"error\":\"provider secret detail\"}"));
        var ex = await Assert.ThrowsAsync<ShortenerApiException>(() => client.CreateGuestAsync(new("https://example.com", null)));
        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("provider secret detail", ex.Message);
    }

    [Fact]
    public async Task Missing_api_address_is_actionable()
    {
        var client = MakeClient(_ => throw new Exception("No network expected"), baseUrl: null);
        var ex = await Assert.ThrowsAsync<ShortenerApiException>(() => client.CreateGuestAsync(new("https://example.com", null)));
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public async Task Nonlocal_http_api_address_is_rejected_before_a_token_is_sent()
    {
        var client = MakeClient(_ => throw new Exception("No network expected"), baseUrl: "http://api.example.test/");
        var ex = await Assert.ThrowsAsync<ShortenerApiException>(() => client.CreateManagedAsync(new("https://example.com", null), User));
        Assert.Contains("not configured", ex.Message);
    }

    private static ShortenerApiClient MakeClient(Func<HttpRequestMessage, HttpResponseMessage> respond, string? baseUrl = "https://api.example.test/")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ShortenerApi:BaseUrl"] = baseUrl }).Build();
        return new(new HttpClient(new DelegateHandler(respond)), new TestTokenProvider(), config, NullLogger<ShortenerApiClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class TestTokenProvider : IManagedApiTokenProvider
    {
        public Task<string> GetTokenAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            Assert.True(user.Identity?.IsAuthenticated);
            return Task.FromResult("test-token");
        }
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
