using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using Shortener.Web.Components;
using Shortener.Web.Services;

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    try
    {
        using var response = await client.GetAsync("http://localhost:8080/health/ready");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException) { Environment.ExitCode = 1; }
    catch (TaskCanceledException) { Environment.ExitCode = 1; }
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
ConfigureTrustedProxy(builder);

var entraConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:TenantId"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:Instance"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientSecret"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["ShortenerApi:Scope"]);
builder.Services.AddSingleton(new IdentityAvailability(entraConfigured));
if (entraConfigured)
{
    builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAd")
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddInMemoryTokenCaches();
    builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme,
        options => options.Scope.Add(builder.Configuration["ShortenerApi:Scope"]!));
    builder.Services.AddScoped<IManagedApiTokenProvider, EntraApiTokenProvider>();
}
else
{
    // Guest creation remains available before an operator configures Entra.
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options => options.LoginPath = "/account/signin");
    builder.Services.AddScoped<IManagedApiTokenProvider, UnconfiguredTokenProvider>();
}
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IShortenerApiClient, ShortenerApiClient>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.Lifetime.ApplicationStarted.Register(() => app.Logger.LogInformation("Service started. Service={Service}", "web"));
app.Lifetime.ApplicationStopping.Register(() => app.Logger.LogInformation("Service stopping. Service={Service}", "web"));

app.MapGet("/health/live", () => Results.Ok()).AllowAnonymous();
app.MapGet("/health/ready", async (IConfiguration configuration, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    var configured = configuration["ShortenerApi:BaseUrl"];
    if (!Uri.TryCreate(configured, UriKind.Absolute, out var apiBase) ||
        !Uri.TryCreate(configuration["PublicLinks:BaseUrl"], UriKind.Absolute, out _))
        return Results.StatusCode(503);
    try
    {
        using var client = clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(3);
        using var response = await client.GetAsync(new Uri(apiBase, "health/ready"), cancellationToken);
        return response.IsSuccessStatusCode ? Results.Ok() : Results.StatusCode(503);
    }
    catch (HttpRequestException) { return Results.StatusCode(503); }
    catch (TaskCanceledException) { return Results.StatusCode(503); }
}).AllowAnonymous();

app.MapGet("/account/signin", (IdentityAvailability identity) =>
    identity.Configured
        ? Results.Challenge(new AuthenticationProperties { RedirectUri = "/dashboard" }, ["OpenIdConnect"])
        : Results.Problem("Managed sign-in is not configured.", statusCode: 503));
app.MapGet("/account/renew", (IdentityAvailability identity) =>
    identity.Configured
        ? Results.Challenge(new AuthenticationProperties { RedirectUri = "/dashboard?reauth=1" }, ["OpenIdConnect"])
        : Results.Problem("Managed sign-in is not configured.", statusCode: 503));
app.MapPost("/account/signout", async (HttpContext context, IAntiforgery antiforgery, IdentityAvailability identity) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    return identity.Configured
        ? Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, ["Cookies", "OpenIdConnect"])
        : Results.Redirect("/");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static void ConfigureTrustedProxy(WebApplicationBuilder builder)
{
    var proxyHost = builder.Configuration["ForwardedHeaders:KnownProxyHost"];
    if (string.IsNullOrWhiteSpace(proxyHost)) return;
    var addresses = System.Net.Dns.GetHostAddresses(proxyHost);
    if (addresses.Length == 0) throw new InvalidOperationException("The configured reverse proxy could not be resolved.");
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var address in addresses) options.KnownProxies.Add(address);
        var publicUrl = builder.Configuration["PublicLinks:BaseUrl"];
        if (Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri)) options.AllowedHosts.Add(uri.Host);
    });
}
