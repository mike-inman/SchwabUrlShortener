using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Shortener.Application;
using Shortener.Application.Interfaces;
using Shortener.Domain;
using Shortener.Infrastructure;
using Shortener.Shared;
using Shortener.Infrastructure.LiveUpdates;

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
if (!args.Contains("--migrate", StringComparer.Ordinal)) builder.Services.AddLiveUpdates(builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
var proxyTrustMode = ScaleoutHostConfiguration.ConfigureTrustedProxy(builder.Services, builder.Configuration);
var migrationOnly = args.Contains("--migrate", StringComparer.Ordinal);
var dataProtectionMode = migrationOnly ? "MigrationOnly" :
    ScaleoutHostConfiguration.AddConfiguredDataProtection(builder.Services, builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("ShortenerDb");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("ConnectionStrings:ShortenerDb must be configured for PostgreSQL persistence.");
    return new NpgsqlDataSourceBuilder(connectionString).Build();
});
builder.Services.AddDbContextFactory<ShortenerDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddSingleton<IUrlShortenerRepository, PostgresUrlShortenerRepository>();
builder.Services.AddSingleton<IWorkspaceLinkAccess, PostgresWorkspaceLinkAccess>();
builder.Services.AddSingleton<PostgresCustomerWorkspaces>();
builder.Services.AddSingleton<IRedirectCache>(sp => new RedisRedirectCache(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Redis"),
    sp.GetRequiredService<IConfiguration>()["RedirectCache:Namespace"]));
builder.Services.AddSingleton<IUrlShortenerService, UrlShortenerService>();
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
var identityMode = builder.Configuration["Identity:Mode"];
if (!string.IsNullOrWhiteSpace(identityMode) &&
    !string.Equals(identityMode, "Customer", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(identityMode, "Workforce", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Identity:Mode must be Customer or Workforce.");
var customerIdentity = string.Equals(identityMode, "Customer", StringComparison.OrdinalIgnoreCase);
if (customerIdentity)
{
    var instance = builder.Configuration["AzureAd:Instance"];
    var tenantId = builder.Configuration["AzureAd:TenantId"];
    var clientId = builder.Configuration["AzureAd:ClientId"];
    if (!Uri.TryCreate(instance, UriKind.Absolute, out var authorityHost) || authorityHost.Scheme != "https" ||
        !authorityHost.Host.EndsWith(".ciamlogin.com", StringComparison.OrdinalIgnoreCase) ||
        !Guid.TryParse(tenantId, out _) || !Guid.TryParse(clientId, out _))
        throw new InvalidOperationException("Customer API identity requires a ciamlogin.com instance, tenant ID, and API client ID.");
    var authority = instance!.TrimEnd('/') + "/" + tenantId + "/v2.0";
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudiences = [clientId, "api://" + clientId],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "sub"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var claims = context.Principal?.Claims ?? [];
                if (claims.Count(x => x.Type == "iss") != 1 || claims.Count(x => x.Type == "sub") != 1 ||
                    claims.SingleOrDefault(x => x.Type == "sub")?.Value is not { Length: > 0 } subject ||
                    subject.Length > 500 || claims.SingleOrDefault(x => x.Type == "ver")?.Value != "2.0" ||
                    !claims.Any(x => x.Type == "scp" && x.Value.Split(' ').Contains("access_as_user")))
                    context.Fail("Customer access token claims are invalid.");
                return Task.CompletedTask;
            }
        };
    });
}
else
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration, "AzureAd");
    builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.MapInboundClaims = false);
}
builder.Services.AddAuthorization(options =>
{
    var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser();
    if (customerIdentity)
        policy.RequireAssertion(context => context.User.FindAll("scp")
            .Any(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("access_as_user")));
    else
        policy.RequireScope("access_as_user");
    options.DefaultPolicy = policy.Build();
});

var app = builder.Build();
app.Logger.LogInformation("Scale-out configuration. DataProtectionMode={DataProtectionMode} ForwardedHeadersTrustMode={ForwardedHeadersTrustMode}", dataProtectionMode, proxyTrustMode);
if (!migrationOnly)
    _ = app.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()
        .CreateProtector("startup-check").Protect("startup-check");
// A real app registration is required. No demo tenant or identity is installed at runtime.
if (string.IsNullOrWhiteSpace(app.Configuration["AzureAd:ClientId"]))
    throw new InvalidOperationException("AzureAd:ClientId must be configured with the Entra API app registration before starting the API.");
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("ShortenerDb")))
    throw new InvalidOperationException("ConnectionStrings:ShortenerDb must be configured for PostgreSQL persistence.");
_ = app.Services.GetRequiredService<NpgsqlDataSource>();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError("Request failed. ExceptionType={ExceptionType} StackTrace={StackTrace} CorrelationId={CorrelationId}",
        exception?.GetType().Name, exception?.StackTrace, context.TraceIdentifier);
    await Results.Problem(title: "Unable to process the request.", statusCode: 500).ExecuteAsync(context);
}));
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/urls/managed") &&
        context.User.Identity?.IsAuthenticated != true)
        app.Logger.LogWarning("Managed operation rejected: unauthenticated. CorrelationId={CorrelationId}", context.TraceIdentifier);
    await next(context);
});
app.UseAuthorization();
app.Lifetime.ApplicationStarted.Register(() => app.Logger.LogInformation("Service started. Service={Service}", "api"));
app.Lifetime.ApplicationStopping.Register(() => app.Logger.LogInformation("Service stopping. Service={Service}", "api"));

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.MapGet("/api/me/workspaces", async (ClaimsPrincipal principal, HttpContext context,
    PostgresCustomerWorkspaces workspaces, CancellationToken cancellationToken) =>
{
    if (!TryGetValidatedIdentity(principal, context, out var issuer, out var subject)) return Results.Forbid();
    return Results.Ok(await workspaces.GetOrCreateAsync(issuer, subject, cancellationToken));
}).WithName("GetMyWorkspaces").RequireAuthorization();

app.MapPost("/api/urls/guest", async (CreateUrlRequest request, IUrlShortenerService service, CancellationToken cancellationToken) =>
{
    try
    {
        var mapping = await service.CreateGuestAsync(request.OriginalUrl, request.ShortCode, cancellationToken);
        return Results.Created($"/r/{mapping.ShortCode}", ToCreateResponse(mapping));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new ApiError(ex.Message)); }
    catch (ShortCodeConflictException ex) { return Results.Conflict(new ApiError(ex.Message)); }
}).WithName("CreateGuestLink").AllowAnonymous();

app.MapPost("/api/urls/managed", async (CreateUrlRequest request, ClaimsPrincipal principal, HttpContext context, IUrlShortenerService service, ILinkChangePublisher publisher, CancellationToken cancellationToken) =>
{
    if (customerIdentity) return Results.NotFound();
    if (!TryGetTenant(principal, context, out var tenantId)) return Results.Forbid();
    try
    {
        var outcome = await service.CreateManagedWithOutcomeAsync(tenantId, request.OriginalUrl, request.ShortCode, cancellationToken);
        if (!outcome.AlreadyExists)
            await publisher.PublishAsync(new LinkChange(LinkChangeScope.Tenant, tenantId, outcome.Mapping.ShortCode), cancellationToken);
        return outcome.AlreadyExists
            ? Results.Ok(ToCreateResponse(outcome.Mapping))
            : Results.Created($"/api/urls/managed/{outcome.Mapping.ShortCode}", ToCreateResponse(outcome.Mapping));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new ApiError(ex.Message)); }
    catch (ShortCodeConflictException ex) { return Results.Conflict(new ApiError(ex.Message)); }
}).WithName("CreateManagedLink").RequireAuthorization();

app.MapGet("/r/{shortCode}", async (string shortCode, IUrlShortenerService service, CancellationToken cancellationToken) =>
{
    var mapping = await service.ResolveAsync(shortCode, cancellationToken);
    return mapping is null ? Results.NotFound(new ApiError("Link not found or expired.")) : Results.Redirect(mapping.OriginalUrl);
}).WithName("ResolveShortUrl").AllowAnonymous();

app.MapGet("/api/urls/managed/{shortCode}", async (string shortCode, ClaimsPrincipal principal, HttpContext context, IUrlShortenerService service, CancellationToken cancellationToken) =>
{
    if (customerIdentity) return Results.NotFound();
    if (!TryGetTenant(principal, context, out var tenantId)) return Results.Forbid();
    var mapping = await service.GetManagedAsync(tenantId, shortCode, cancellationToken);
    return mapping is null ? Results.NotFound(new ApiError("Managed link not found.")) : Results.Ok(ToManagedResponse(mapping));
}).WithName("GetManagedLink").RequireAuthorization();

app.MapGet("/api/urls/managed/{shortCode}/analytics", async (string shortCode, ClaimsPrincipal principal, HttpContext context, IUrlShortenerService service, CancellationToken cancellationToken) =>
{
    if (customerIdentity) return Results.NotFound();
    if (!TryGetTenant(principal, context, out var tenantId)) return Results.Forbid();
    var mapping = await service.GetManagedAsync(tenantId, shortCode, cancellationToken);
    return mapping is null ? Results.NotFound(new ApiError("Managed link not found.")) : Results.Ok(ToManagedResponse(mapping));
}).WithName("GetManagedLinkAnalytics").RequireAuthorization();

app.MapGet("/api/urls/managed/analytics", async (ClaimsPrincipal principal, HttpContext context, IUrlShortenerService service, CancellationToken cancellationToken) =>
{
    if (customerIdentity) return Results.NotFound();
    if (!TryGetTenant(principal, context, out var tenantId)) return Results.Forbid();
    var mappings = await service.GetManagedAnalyticsAsync(tenantId, cancellationToken);
    return Results.Ok(mappings.Select(ToManagedResponse).ToArray());
}).WithName("GetManagedAnalytics").RequireAuthorization();

var workspaceManaged = app.MapGroup("/api/workspaces/{workspaceId:guid}/urls/managed").RequireAuthorization();
workspaceManaged.MapPost("", async (Guid workspaceId, CreateUrlRequest request, ClaimsPrincipal principal,
    HttpContext context, IUrlShortenerService service, ILinkChangePublisher publisher, CancellationToken cancellationToken) =>
{
    if (!TryGetValidatedIdentity(principal, context, out var issuer, out var subject)) return Results.Forbid();
    try
    {
        var outcome = await service.CreateWorkspaceManagedAsync(issuer, subject, workspaceId,
            request.OriginalUrl, request.ShortCode, cancellationToken);
        if (outcome is null) return Results.NotFound(new ApiError("Workspace not found."));
        if (!outcome.Value.AlreadyExists)
            await publisher.PublishAsync(new LinkChange(LinkChangeScope.Workspace, workspaceId,
                outcome.Value.Mapping.ShortCode), cancellationToken);
        return outcome.Value.AlreadyExists ? Results.Ok(ToCreateResponse(outcome.Value.Mapping))
            : Results.Created($"/api/workspaces/{workspaceId}/urls/managed/{outcome.Value.Mapping.ShortCode}",
                ToCreateResponse(outcome.Value.Mapping));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new ApiError(ex.Message)); }
    catch (ShortCodeConflictException ex) { return Results.Conflict(new ApiError(ex.Message)); }
}).WithName("CreateWorkspaceManagedLink");

workspaceManaged.MapGet("/analytics", async (Guid workspaceId, ClaimsPrincipal principal, HttpContext context,
    IUrlShortenerService service, CancellationToken cancellationToken) =>
{
    if (!TryGetValidatedIdentity(principal, context, out var issuer, out var subject)) return Results.Forbid();
    var mappings = await service.GetWorkspaceAnalyticsAsync(issuer, subject, workspaceId, cancellationToken);
    return mappings is null ? Results.NotFound(new ApiError("Workspace not found."))
        : Results.Ok(mappings.Select(ToManagedResponse).ToArray());
}).WithName("GetWorkspaceManagedAnalytics");

workspaceManaged.MapGet("/{shortCode}", async (Guid workspaceId, string shortCode, ClaimsPrincipal principal,
    HttpContext context, IUrlShortenerService service, CancellationToken cancellationToken) =>
{
    if (!TryGetValidatedIdentity(principal, context, out var issuer, out var subject)) return Results.Forbid();
    var mapping = await service.GetWorkspaceManagedAsync(issuer, subject, workspaceId, shortCode, cancellationToken);
    return mapping is null ? Results.NotFound(new ApiError("Managed link not found.")) : Results.Ok(ToManagedResponse(mapping));
}).WithName("GetWorkspaceManagedLink");

workspaceManaged.MapGet("/{shortCode}/analytics", async (Guid workspaceId, string shortCode, ClaimsPrincipal principal,
    HttpContext context, IUrlShortenerService service, CancellationToken cancellationToken) =>
{
    if (!TryGetValidatedIdentity(principal, context, out var issuer, out var subject)) return Results.Forbid();
    var mapping = await service.GetWorkspaceManagedAsync(issuer, subject, workspaceId, shortCode, cancellationToken);
    return mapping is null ? Results.NotFound(new ApiError("Managed link not found.")) : Results.Ok(ToManagedResponse(mapping));
}).WithName("GetWorkspaceManagedLinkAnalytics");
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ShortenerDbContext>>().CreateDbContextAsync();
    await db.Database.MigrateAsync();
    return;
}

app.Run();

static bool TryGetTenant(ClaimsPrincipal principal, HttpContext context, out Guid tenantId)
{
    var value = principal.FindFirstValue("tid") ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
    if (Guid.TryParse(value, out tenantId) && tenantId != Guid.Empty) return true;
    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TenantBoundary")
        .LogWarning("Managed operation rejected: invalid tenant context. CorrelationId={CorrelationId}", context.TraceIdentifier);
    return false;
}

static bool TryGetValidatedIdentity(ClaimsPrincipal principal, HttpContext context, out string issuer, out string subject)
{
    var issuers = principal.FindAll("iss").ToArray();
    var subjects = principal.FindAll("sub").ToArray();
    issuer = issuers.Length == 1 ? issuers[0].Value : string.Empty;
    subject = subjects.Length == 1 ? subjects[0].Value : string.Empty;
    if (principal.Identity?.IsAuthenticated == true &&
        Uri.TryCreate(issuer, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        !string.IsNullOrWhiteSpace(subject) && subject.Length <= 500 && issuer.Length <= 500)
        return true;
    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("WorkspaceBoundary")
        .LogWarning("Workspace operation rejected: missing validated identity claims. CorrelationId={CorrelationId}", context.TraceIdentifier);
    return false;
}

static CreateUrlResponse ToCreateResponse(UrlMapping mapping) => new(
    mapping.ShortCode, mapping.OriginalUrl, $"/r/{mapping.ShortCode}", mapping.CreatedAtUtc, mapping.ExpiresAtUtc);
static ManagedUrlResponse ToManagedResponse(UrlMapping mapping) => new(
    mapping.ShortCode, mapping.OriginalUrl, mapping.CreatedAtUtc, mapping.LastAccessedAtUtc, mapping.HitCount);

public record CreateUrlRequest(string OriginalUrl, string? ShortCode = null);
public record CreateUrlResponse(string ShortCode, string OriginalUrl, string ShortUrl, DateTimeOffset CreatedAtUtc, DateTimeOffset? ExpiresAtUtc);
public record ManagedUrlResponse(string ShortCode, string OriginalUrl, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastAccessedAtUtc, int HitCount);
public record ApiError(string Error);
public partial class Program { }

public sealed class PostgresHealthCheck(IDbContextFactory<ShortenerDbContext> contextFactory, ILogger<PostgresHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            return pending.Any() ? HealthCheckResult.Unhealthy("PostgreSQL migrations pending.") : HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("PostgreSQL readiness failed. ExceptionType={ExceptionType}", ex.GetType().Name);
            return HealthCheckResult.Unhealthy("PostgreSQL unavailable.");
        }
    }
}

public sealed class RedisHealthCheck(IRedirectCache cache, ILogger<RedisHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (await cache.IsAvailableAsync(cancellationToken)) return HealthCheckResult.Healthy();
        logger.LogWarning("Redis readiness failed; redirect lookup will use PostgreSQL.");
        return HealthCheckResult.Unhealthy("Redis unavailable.");
    }
}
