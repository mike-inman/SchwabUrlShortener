using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shortener.Shared;
using Shortener.Web.Components.Pages;
using Xunit;

namespace Shortener.WebTests;

public sealed class HardeningHttpTests
{
    [Fact]
    public void Telemetry_has_no_exporter_without_a_connection_string()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "";
        builder.Configuration["OpenTelemetry:SamplingRatio"] = "1.0";
        builder.Configuration["OpenTelemetry:ConsoleExporter"] = "false";
        var configured = TelemetryHostConfiguration.AddConfiguredTelemetry(builder, "shortener-web");
        Assert.Equal("None", configured.Exporter);
        Assert.Equal(1.0f, configured.SamplingRatio);
    }

    [Fact]
    public async Task Web_redirects_www_and_serves_health_on_www()
    {
        using var factory = new WebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://www.example.test"), AllowAutoRedirect = false
        });
        using var redirect = await client.GetAsync("/dashboard?view=recent%20links");
        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.Equal("https://example.test/dashboard?view=recent%20links", redirect.Headers.Location?.OriginalString);
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Null(health.Headers.Location);
        using var apex = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false
        });
        using var apexHome = await apex.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, apexHome.StatusCode);
        Assert.Null(apexHome.Headers.Location);
    }

    [Fact]
    public async Task Web_home_sends_csp_and_other_defensive_headers()
    {
        using var factory = new WebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false
        });
        using var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("connect-src 'self' wss:;", csp);
        Assert.Contains("form-action 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("script-src 'self' 'nonce-", csp);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("camera=(), microphone=(), geolocation=()", response.Headers.GetValues("Permissions-Policy").Single());
        using var dashboard = await client.GetAsync("/dashboard");
        Assert.Contains("frame-ancestors 'none'", dashboard.Headers.GetValues("Content-Security-Policy").Single());
        Assert.DoesNotContain("fonts.googleapis.com", csp);
        Assert.DoesNotContain("fonts.gstatic.com", csp);
        Assert.DoesNotContain("fonts.googleapis.com", await response.Content.ReadAsStringAsync());
        using var privacy = await client.GetAsync("/privacy");
        Assert.Equal(HttpStatusCode.OK, privacy.StatusCode);
        var privacyHtml = await privacy.Content.ReadAsStringAsync();
        Assert.Contains("Privacy &amp; Cookies", privacyHtml);
        Assert.Contains($"© {DateTime.UtcNow.Year} Ziplio", privacyHtml);
        Assert.Contains("/contact", privacyHtml);
        using var font = await client.GetAsync("/fonts/BricolageGrotesque.ttf");
        Assert.Equal(HttpStatusCode.OK, font.StatusCode);
    }

    [Fact]
    public async Task Web_csp_allows_only_configured_https_identity_origin_for_navigation_fallback()
    {
        using var factory = new WebFactory("https://login.example.test/tenant/v2.0");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false
        });
        using var response = await client.GetAsync("/");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("connect-src 'self' wss: https://login.example.test;", csp);
        Assert.Contains("form-action 'self' https://login.example.test", csp);
        Assert.DoesNotContain("login.example.test/tenant", csp);
    }

    [Fact]
    public async Task Favicon_assets_are_served_under_same_origin_csp()
    {
        using var factory = new WebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false
        });
        foreach (var path in new[] { "/favicon.ico", "/favicon.png", "/favicon.svg", "/apple-touch-icon.png" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("img-src 'self' data:", response.Headers.GetValues("Content-Security-Policy").Single());
        }
    }

    [Fact]
    public async Task Web_guest_form_post_is_limited_before_component_handling()
    {
        using var factory = new WebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false
        });
        using var page = await client.GetAsync("/");
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        FormUrlEncodedContent Form() => new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["_handler"] = "guest-link",
            ["form.OriginalUrl"] = "https://example.org/"
        });
        using var first = await client.PostAsync("/", Form());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var rejected = await client.PostAsync("/", Form());
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out _));
    }

    private sealed class WebFactory(string? authority = null) : WebApplicationFactory<SignedOut>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1");
            builder.UseSetting("PublicLinks:BaseUrl", "https://example.test/");
            builder.UseSetting("RateLimiting:GuestCreate:PermitLimit", "1");
            if (authority is not null) builder.UseSetting("AzureAd:Authority", authority);
        }
    }
}
