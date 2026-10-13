using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;
using Npgsql;
using Shortener.Application;
using Shortener.Application.Interfaces;
using Shortener.Domain;
using Shortener.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton<IUrlShortenerService, UrlShortenerService>();
builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration, "AzureAd");
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireScope("access_as_user")
        .Build();
});

var app = builder.Build();
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

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

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

app.MapPost("/api/urls/managed", async (CreateUrlRequest request, ClaimsPrincipal principal, HttpContext context, IUrlShortenerService service, CancellationToken cancellationToken) =>
{
    if (!TryGetTenant(principal, context, out var tenantId)) return Results.Forbid();
    try
    {
        var outcome = await service.CreateManagedWithOutcomeAsync(tenantId, request.OriginalUrl, request.ShortCode, cancellationToken);
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
    if (!TryGetTenant(principal, context, out var tenantId)) return Results.Forbid();
    var mapping = await service.GetManagedAsync(tenantId, shortCode, cancellationToken);
    return mapping is null ? Results.NotFound(new ApiError("Managed link not found.")) : Results.Ok(ToManagedResponse(mapping));
}).WithName("GetManagedLink").RequireAuthorization();

app.MapGet("/api/urls/managed/{shortCode}/analytics", async (string shortCode, ClaimsPrincipal principal, HttpContext context, IUrlShortenerService service, CancellationToken cancellationToken) =>
{
    if (!TryGetTenant(principal, context, out var tenantId)) return Results.Forbid();
    var mapping = await service.GetManagedAsync(tenantId, shortCode, cancellationToken);
    return mapping is null ? Results.NotFound(new ApiError("Managed link not found.")) : Results.Ok(ToManagedResponse(mapping));
}).WithName("GetManagedLinkAnalytics").RequireAuthorization();

app.MapGet("/api/urls/managed/analytics", async (ClaimsPrincipal principal, HttpContext context, IUrlShortenerService service, CancellationToken cancellationToken) =>
{
    if (!TryGetTenant(principal, context, out var tenantId)) return Results.Forbid();
    var mappings = await service.GetManagedAnalyticsAsync(tenantId, cancellationToken);
    return Results.Ok(mappings.Select(ToManagedResponse).ToArray());
}).WithName("GetManagedAnalytics").RequireAuthorization();

app.Run();

static bool TryGetTenant(ClaimsPrincipal principal, HttpContext context, out Guid tenantId)
{
    var value = principal.FindFirstValue("tid") ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
    if (Guid.TryParse(value, out tenantId) && tenantId != Guid.Empty) return true;
    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TenantBoundary")
        .LogWarning("Managed operation rejected: invalid tenant context. CorrelationId={CorrelationId}", context.TraceIdentifier);
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
