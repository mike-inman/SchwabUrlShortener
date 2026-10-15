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
        Assert.Equal("https://go.example.test/r/abc", result.PublicUrl.ToString());
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
        Assert.False((await client.CreateManagedAsync(new("https://example.com", "abc"), User)).AlreadyExists);
        var links = await client.GetManagedLinksAsync(User);
        await client.GetManagedLinkAsync("abc", User);
        var analytics = await client.GetManagedAnalyticsAsync("abc", User);
        Assert.Single(links);
        Assert.Equal(3, analytics.HitCount);
        Assert.Equal(["/api/urls/managed", "/api/urls/managed/analytics", "/api/urls/managed/abc", "/api/urls/managed/abc/analytics"], paths);
    }

    [Fact]
    public async Task Managed_existing_link_is_reported_as_existing()
    {
        var client = MakeClient(_ => Json(HttpStatusCode.OK, CreatedJson));
        var result = await client.CreateManagedAsync(new("https://example.com", "abc"), User);
        Assert.True(result.AlreadyExists);
        Assert.Equal("abc", result.Link.ShortCode);
    }

    [Fact]
    public async Task Customer_calls_use_workspace_routes_and_bearer_token()
    {
        var workspaceId = Guid.NewGuid();
        var paths = new List<string>();
        var client = MakeClient(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return request.RequestUri.AbsolutePath == "/api/me/workspaces"
                ? Json(HttpStatusCode.OK, $"[{{\"id\":\"{workspaceId}\",\"name\":\"Personal\",\"kind\":0}}]")
                : request.Method == HttpMethod.Post ? Json(HttpStatusCode.Created, CreatedJson)
                : request.RequestUri.AbsolutePath.EndsWith("/analytics") && request.RequestUri.AbsolutePath.EndsWith("managed/analytics")
                    ? Json(HttpStatusCode.OK, "[" + ManagedJson + "]")
                    : Json(HttpStatusCode.OK, ManagedJson);
        });
        Assert.Equal(workspaceId, Assert.Single(await client.GetWorkspacesAsync(User)).Id);
        await client.CreateWorkspaceManagedAsync(workspaceId, new("https://example.com", "abc"), User);
        Assert.Single(await client.GetWorkspaceManagedLinksAsync(workspaceId, User));
        await client.GetWorkspaceManagedLinkAsync(workspaceId, "abc", User);
        await client.GetWorkspaceManagedAnalyticsAsync(workspaceId, "abc", User);
        Assert.Equal(5, paths.Count);
        Assert.All(paths.Skip(1), path => Assert.StartsWith($"/api/workspaces/{workspaceId}/urls/managed", path));
        Assert.DoesNotContain(paths, path => path.StartsWith("/api/urls/managed", StringComparison.Ordinal));
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

    [Theory]
    [InlineData("Original URL must be an absolute http or https URL. (Parameter 'originalUrl')", CreateLinkField.DestinationUrl)]
    [InlineData("The short code must be 4 to 12 ASCII letters or digits. (Parameter 'shortCode')", CreateLinkField.ShortCode)]
    public async Task Api_validation_reason_identifies_the_field_without_echoing_it(string reason, CreateLinkField field)
    {
        var client = MakeClient(_ => Json(HttpStatusCode.BadRequest, System.Text.Json.JsonSerializer.Serialize(new { error = reason })));
        var ex = await Assert.ThrowsAsync<ShortenerApiException>(() => client.CreateGuestAsync(new("https://example.com", "bad!")));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(field, ex.Field);
        Assert.DoesNotContain(reason, ex.Message);
    }

    [Fact]
    public async Task Unidentified_api_400_keeps_the_existing_generic_fallback()
    {
        var client = MakeClient(_ => Json(HttpStatusCode.BadRequest, "{\"error\":\"Unrelated error\"}"));
        var ex = await Assert.ThrowsAsync<ShortenerApiException>(() => client.CreateGuestAsync(new("https://example.com", null)));
        Assert.Null(ex.Field);
        Assert.Equal("Check the destination URL (it must start with http:// or https://) and the custom code (4–12 letters or numbers, no spaces or symbols).", ex.Message);
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

    [Fact]
    public async Task Explicit_internal_http_api_never_appears_in_public_link()
    {
        var client = MakeClient(request =>
        {
            Assert.Equal("http://api:8080/api/urls/guest", request.RequestUri!.ToString());
            return Json(HttpStatusCode.Created, CreatedJson);
        }, "http://api:8080/", allowedHosts: "api", publicUrl: "http://localhost:8080/");
        var result = await client.CreateGuestAsync(new("https://example.com", "abc"));
        Assert.Equal("http://localhost:8080/r/abc", result.PublicUrl.ToString());
        Assert.DoesNotContain("api:8080", result.PublicUrl.ToString());
    }

    [Fact]
    public void Missing_or_unsafe_public_origin_is_rejected()
    {
        Assert.Throws<ShortenerApiException>(() => MakeClient(_ => throw new Exception(), publicUrl: null).PublicUrl("abc"));
        Assert.Throws<ShortenerApiException>(() => MakeClient(_ => throw new Exception(), publicUrl: "http://go.example.test/").PublicUrl("abc"));
        Assert.Throws<ShortenerApiException>(() => MakeClient(_ => throw new Exception(), publicUrl: "https://go.example.test/other/").PublicUrl("abc"));
    }

    [Theory]
    [InlineData("http://api:8080", null, false)]
    [InlineData("http://api:8080", "api", true)]
    [InlineData("http://api:8080", "other, API ", true)]
    [InlineData("http://unlisted:8080", "api", false)]
    [InlineData("http://api.evil.test:8080", "api", false)]
    [InlineData("https://api.example.test", null, true)]
    [InlineData("http://localhost:8080", null, true)]
    public void Api_address_validation_uses_exact_internal_host_allowance(
        string baseUrl, string? allowedHosts, bool expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ShortenerApi:BaseUrl"] = baseUrl,
            ["ShortenerApi:AllowedInternalHttpHosts"] = allowedHosts
        }).Build();
        Assert.Equal(expected, ShortenerApiAddress.TryResolve(config, out var resolved, out _));
        Assert.Equal(expected, resolved is not null);
    }

    [Fact]
    public async Task Explicit_compose_host_allowance_enables_signed_in_workspace_calls()
    {
        var client = MakeClient(request =>
        {
            Assert.Equal("api", request.RequestUri?.Host);
            Assert.Equal("/api/me/workspaces", request.RequestUri?.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return Json(HttpStatusCode.OK, "[]");
        }, baseUrl: "http://api:8080", allowedHosts: "api");
        Assert.Empty(await client.GetWorkspacesAsync(User));
    }

    private static ShortenerApiClient MakeClient(Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? baseUrl = "https://api.example.test/", string? allowedHosts = null,
        string? publicUrl = "https://go.example.test/")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ShortenerApi:BaseUrl"] = baseUrl,
            ["ShortenerApi:AllowedInternalHttpHosts"] = allowedHosts,
            ["PublicLinks:BaseUrl"] = publicUrl
        }).Build();
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
