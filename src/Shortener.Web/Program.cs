using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using Shortener.Shared;
using Shortener.Web.Components;
using Shortener.Web.Services;
using ApexCharts;
using StackExchange.Redis;
using Shortener.Web.Services.LiveUpdates;

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
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
HardeningHostConfiguration.AddConfiguredRateLimiting(builder.Services, builder.Configuration, web: true);
builder.Services.AddLiveUpdates(builder.Configuration);
var identityMode = builder.Configuration["Identity:Mode"];
if (!string.IsNullOrWhiteSpace(identityMode) &&
    !string.Equals(identityMode, "Customer", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(identityMode, "Workforce", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Identity:Mode must be Customer or Workforce.");
var customerIdentity = string.Equals(identityMode, "Customer", StringComparison.OrdinalIgnoreCase);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
var telemetry = TelemetryHostConfiguration.AddConfiguredTelemetry(builder, "shortener-web");
var proxyTrustMode = ScaleoutHostConfiguration.ConfigureTrustedProxy(builder.Services, builder.Configuration);
var dataProtectionMode = ScaleoutHostConfiguration.AddConfiguredDataProtection(builder.Services, builder.Configuration);
var distributedCache = TokenCacheConfiguration.AddConfiguredTokenCache(builder.Services, builder.Configuration);

var entraConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:TenantId"])
    && !string.IsNullOrWhiteSpace(builder.Configuration[customerIdentity ? "AzureAd:Authority" : "AzureAd:Instance"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientSecret"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["ShortenerApi:Scope"]);
builder.Services.AddSingleton(new IdentityAvailability(entraConfigured, customerIdentity));
if (entraConfigured)
{
    var tokens = builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAd")
        .EnableTokenAcquisitionToCallDownstreamApi();
    if (distributedCache) tokens.AddDistributedTokenCaches();
    else tokens.AddInMemoryTokenCaches();
    builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme,
        options => options.Scope.Add(builder.Configuration["ShortenerApi:Scope"]!));
    builder.Services.Configure<AuthenticationOptions>(options =>
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme);
    if (customerIdentity)
    {
        builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            var previous = options.Events.OnRedirectToIdentityProviderForSignOut;
            options.Events.OnRedirectToIdentityProviderForSignOut = async context =>
            {
                if (previous is not null) await previous(context);
                SignOutHints.Apply(context.HttpContext, context.ProtocolMessage);
            };
        });
    }
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
builder.Services.AddScoped<WorkspaceNavigationState>();
builder.Services.AddApexCharts();
builder.Services.AddHttpClient<IShortenerApiClient, ShortenerApiClient>();
builder.Services.AddHttpClient<IContactInboxApiClient, ContactInboxApiClient>();
builder.Services.AddHttpClient<ContactApiClient>();
builder.Services.AddHttpClient("availability", client => client.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
app.Logger.LogInformation("Scale-out configuration. DataProtectionMode={DataProtectionMode} TokenCacheMode={TokenCacheMode} ForwardedHeadersTrustMode={ForwardedHeadersTrustMode}",
    dataProtectionMode, distributedCache ? "Distributed" : "InMemory", proxyTrustMode);
app.Logger.LogInformation("Telemetry configuration. Exporter={Exporter} SamplingRatio={SamplingRatio}", telemetry.Exporter, telemetry.SamplingRatio);
_ = app.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()
    .CreateProtector("startup-check").Protect("startup-check");
if (distributedCache)
    await app.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().PingAsync();
if (!ShortenerApiAddress.TryResolve(app.Configuration, out _, out var apiAddressReason))
    app.Logger.LogWarning("Shortener API base address rejected. Reason={Reason}. Web readiness will fail.", apiAddressReason);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseForwardedHeaders();
app.UseSecurityHeaders(web: true, app.Configuration);
app.UsePublicHostRedirect(app.Configuration);
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["Cache-Control"] = "private, no-store";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
            return Task.CompletedTask;
        });
    }
    await next();
});
app.UseRateLimiter();
app.UseAuthorization();
app.UseAntiforgery();
app.Lifetime.ApplicationStarted.Register(() => app.Logger.LogInformation("Service started. Service={Service}", "web"));
app.Lifetime.ApplicationStopping.Register(() => app.Logger.LogInformation("Service stopping. Service={Service}", "web"));

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (IHttpClientFactory clients, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!ShortenerApiAddress.TryResolve(configuration, out var apiBase, out _) ||
        !Uri.TryCreate(configuration["PublicLinks:BaseUrl"], UriKind.Absolute, out _))
        return Results.Problem(title: "Shortener API or public link address is invalid or not allowed.", statusCode: 503);
    try
    {
        using var response = await clients.CreateClient("availability").GetAsync(
            new Uri(apiBase!, "/health/ready"), cancellationToken);
        return response.IsSuccessStatusCode
            ? Results.Ok(new { status = "ready" })
            : Results.Problem(title: "Shortener API is unavailable.", statusCode: 503);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        app.Logger.LogWarning("Web readiness failed. Dependency=ShortenerApi ExceptionType={ExceptionType}", ex.GetType().Name);
        return Results.Problem(title: "Shortener API is unavailable.", statusCode: 503);
    }
}).AllowAnonymous();
app.MapGet("/health/identity", (IdentityAvailability identity) => Results.Ok(new { configured = identity.Configured })).AllowAnonymous();

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
    SignOutHints.Capture(context);
    return identity.Configured
        ? Results.SignOut(new AuthenticationProperties { RedirectUri = "/signed-out" }, ["Cookies", "OpenIdConnect"])
        : Results.Redirect("/");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
