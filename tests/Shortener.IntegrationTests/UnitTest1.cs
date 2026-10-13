using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Shortener.IntegrationTests;

public class ShortenerApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ShortenerApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateResolveAndAnalyticsFlow_WorksEndToEnd()
    {
        var client = _factory.CreateClient();

        var createRequest = new
        {
            originalUrl = "https://example.com/products?category=books",
            shortCode = "demo123"
        };

        var createResponse = await client.PostAsJsonAsync("/api/urls", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        Assert.Equal("demo123", created.RootElement.GetProperty("shortCode").GetString());

        var resolveResponse = await client.GetAsync("/api/urls/demo123");
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        using var resolved = JsonDocument.Parse(await resolveResponse.Content.ReadAsStringAsync());
        Assert.Equal("https://example.com/products?category=books", resolved.RootElement.GetProperty("originalUrl").GetString());

        var analyticsResponse = await client.GetAsync("/api/urls/demo123/analytics");
        Assert.Equal(HttpStatusCode.OK, analyticsResponse.StatusCode);

        using var analytics = JsonDocument.Parse(await analyticsResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, analytics.RootElement.GetProperty("hitCount").GetInt32());
    }
}
