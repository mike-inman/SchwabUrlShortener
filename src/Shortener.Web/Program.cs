using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Identity.Web;
using Shortener.Web.Components;
using Shortener.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var identityMode = builder.Configuration["Identity:Mode"];
if (!string.IsNullOrWhiteSpace(identityMode) &&
    !string.Equals(identityMode, "Customer", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(identityMode, "Workforce", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Identity:Mode must be Customer or Workforce.");
var customerIdentity = string.Equals(identityMode, "Customer", StringComparison.OrdinalIgnoreCase);

var entraConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:TenantId"])
    && !string.IsNullOrWhiteSpace(builder.Configuration[customerIdentity ? "AzureAd:Authority" : "AzureAd:Instance"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientSecret"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["ShortenerApi:Scope"]);
builder.Services.AddSingleton(new IdentityAvailability(entraConfigured, customerIdentity));
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
builder.Services.AddHttpClient("availability", client => client.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
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
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (IHttpClientFactory clients, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!ShortenerApiAddress.TryResolve(configuration, out var apiBase, out _))
        return Results.Problem(title: "Shortener API address is invalid or not allowed.", statusCode: 503);
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
    return identity.Configured
        ? Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, ["Cookies", "OpenIdConnect"])
        : Results.Redirect("/");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
