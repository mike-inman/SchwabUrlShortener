using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shortener.Analytics;

namespace Shortener.IntegrationTests;

public sealed class HardeningHttpTests
{
    [Fact]
    public async Task Api_www_redirect_preserves_path_and_query_but_exempts_health()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://www.example.test"), AllowAutoRedirect = false
        });
        using var redirect = await client.GetAsync("/r/sample?campaign=one%20two");
        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.Equal("https://example.test/r/sample?campaign=one%20two", redirect.Headers.Location?.OriginalString);
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Null(health.Headers.Location);
        using var apex = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false
        });
        using var apexHealth = await apex.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, apexHealth.StatusCode);
        Assert.Null(apexHealth.Headers.Location);
    }

    [Fact]
    public async Task Api_headers_and_guest_limit_cover_rejections_and_exempt_health()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false
        });
        using var first = await client.PostAsJsonAsync("/api/urls/guest", new { OriginalUrl = "https://example.test/" });
        using var rejected = await client.PostAsJsonAsync("/api/urls/guest", new { OriginalUrl = "https://example.test/" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out _));
        Assert.Equal("nosniff", rejected.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("frame-ancestors 'none'", rejected.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-referrer", rejected.Headers.GetValues("Referrer-Policy").Single());
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Redirect_limit_rejects_without_limiting_health()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false
        });
        using var first = await client.GetAsync("/r/sample");
        using var rejected = await client.GetAsync("/r/sample");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out _));
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public void Kafka_trace_header_preserves_redirect_trace_as_publish_and_consume_parent()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ClickTraceContext.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var redirect = new Activity("redirect").SetIdFormat(ActivityIdFormat.W3C).Start();
        var (publish, headers) = ClickTraceContext.BeginPublish(redirect.Id);
        using (publish)
        {
            Assert.NotNull(publish);
            Assert.Equal(redirect.TraceId, publish.TraceId);
            Assert.Equal(redirect.SpanId, publish.ParentSpanId);
            using var consume = ClickTraceContext.BeginConsume(headers, null);
            Assert.NotNull(consume);
            Assert.Equal(redirect.TraceId, consume.TraceId);
            Assert.Equal(publish.SpanId, consume.ParentSpanId);
        }
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("AzureAd:ClientId", "11111111-1111-1111-1111-111111111111");
            builder.UseSetting("ConnectionStrings:ShortenerDb", "Host=127.0.0.1;Port=1;Database=Schwab_test_local;Username=test;Timeout=1");
            builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1");
            builder.UseSetting("PublicLinks:BaseUrl", "https://example.test/");
            builder.UseSetting("VisitorHash:DailySecret", "integration-test-secret-not-a-real-value");
            builder.UseSetting("RateLimiting:GuestCreate:PermitLimit", "1");
            builder.UseSetting("RateLimiting:Redirect:TokenLimit", "1");
            builder.UseSetting("RateLimiting:Redirect:TokensPerPeriod", "1");
        }
    }
}
