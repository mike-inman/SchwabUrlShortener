using Shortener.Application;
using Shortener.Application.Interfaces;
using Shortener.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<IUrlShortenerRepository, InMemoryUrlShortenerRepository>();
builder.Services.AddSingleton<IUrlShortenerService, UrlShortenerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("/api/urls", async (CreateUrlRequest request, IUrlShortenerService shortenerService, CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.OriginalUrl))
    {
        return Results.BadRequest(new { error = "OriginalUrl is required." });
    }

    try
    {
        var mapping = await shortenerService.CreateAsync(request.OriginalUrl, request.ShortCode, cancellationToken);
        var response = new CreateUrlResponse(
            mapping.ShortCode,
            mapping.OriginalUrl,
            $"https://short.ly/{mapping.ShortCode}",
            mapping.CreatedAtUtc);

        return Results.CreatedAtRoute("ResolveShortUrl", new { shortCode = mapping.ShortCode }, response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
})
.WithName("CreateShortUrl");

app.MapGet("/api/urls/{shortCode}", async (string shortCode, IUrlShortenerService shortenerService, CancellationToken cancellationToken) =>
{
    var mapping = await shortenerService.ResolveAsync(shortCode, cancellationToken);
    if (mapping is null)
    {
        return Results.NotFound(new { error = $"Short code '{shortCode}' was not found." });
    }

    var response = new ResolveUrlResponse(
        mapping.ShortCode,
        mapping.OriginalUrl,
        mapping.CreatedAtUtc,
        mapping.LastAccessedAtUtc,
        mapping.HitCount);

    return Results.Ok(response);
})
.WithName("ResolveShortUrl");

app.MapGet("/api/urls/{shortCode}/analytics", async (string shortCode, IUrlShortenerService shortenerService, CancellationToken cancellationToken) =>
{
    var mapping = await shortenerService.GetByShortCodeAsync(shortCode, cancellationToken);
    if (mapping is null)
    {
        return Results.NotFound(new { error = $"Short code '{shortCode}' was not found." });
    }

    var response = new UrlAnalyticsResponse(
        mapping.ShortCode,
        mapping.OriginalUrl,
        mapping.CreatedAtUtc,
        mapping.LastAccessedAtUtc,
        mapping.HitCount);

    return Results.Ok(response);
})
.WithName("GetShortUrlAnalytics");

app.MapGet("/api/urls/analytics", async (IUrlShortenerService shortenerService, CancellationToken cancellationToken) =>
{
    var mappings = await shortenerService.GetAnalyticsAsync(cancellationToken);
    var response = mappings
        .Select(mapping => new UrlAnalyticsResponse(
            mapping.ShortCode,
            mapping.OriginalUrl,
            mapping.CreatedAtUtc,
            mapping.LastAccessedAtUtc,
            mapping.HitCount))
        .ToArray();

    return Results.Ok(response);
})
.WithName("GetAllAnalytics");

app.Run();

public record CreateUrlRequest(string OriginalUrl, string? ShortCode = null);
public record CreateUrlResponse(string ShortCode, string OriginalUrl, string ShortUrl, DateTimeOffset CreatedAtUtc);
public record ResolveUrlResponse(string ShortCode, string OriginalUrl, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastAccessedAtUtc, int HitCount);
public record UrlAnalyticsResponse(string ShortCode, string OriginalUrl, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastAccessedAtUtc, int HitCount);

public partial class Program { }
