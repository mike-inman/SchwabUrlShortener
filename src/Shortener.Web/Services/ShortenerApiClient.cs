using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace Shortener.Web.Services;

public sealed record CreateLinkRequest(string OriginalUrl, string? ShortCode);
public sealed record CreatedLink(string ShortCode, string OriginalUrl, string ShortUrl,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? ExpiresAtUtc);
public sealed record ManagedLink(string ShortCode, string OriginalUrl, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastAccessedAtUtc, int HitCount);
public sealed record WorkspaceSummary(Guid Id, string Name, int Kind);
public sealed record LinkResult(CreatedLink Link, Uri PublicUrl, bool AlreadyExists = false);
public sealed record InsightPoint(DateTimeOffset BucketStartUtc, long Clicks);
public sealed record InsightGroup(string Name, long Clicks);
public sealed record InsightLastClick(DateTimeOffset OccurredAtUtc, string? ReferrerGroup, string? DeviceClass);
public sealed record LinkInsights(long TotalClicks, long PreviousPeriodTotal, long UniqueVisitors,
    IReadOnlyList<InsightPoint> TimeSeries, IReadOnlyList<InsightGroup> TopReferrers,
    IReadOnlyList<InsightGroup> DeviceShares, InsightLastClick? LastClick,
    IReadOnlyList<InsightPoint> HeatmapHours);
public sealed record LinkSparkline(string ShortCode, IReadOnlyList<InsightPoint> Days);

public interface IShortenerApiClient
{
    Task<LinkResult> CreateGuestAsync(CreateLinkRequest request, CancellationToken cancellationToken = default);
    Task<LinkResult> CreateManagedAsync(CreateLinkRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedLink>> GetManagedLinksAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<ManagedLink> GetManagedLinkAsync(string shortCode, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<ManagedLink> GetManagedAnalyticsAsync(string shortCode, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkspaceSummary>> GetWorkspacesAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    Task<LinkResult> CreateWorkspaceManagedAsync(Guid workspaceId, CreateLinkRequest request, ClaimsPrincipal user,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<IReadOnlyList<ManagedLink>> GetWorkspaceManagedLinksAsync(Guid workspaceId, ClaimsPrincipal user,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<ManagedLink> GetWorkspaceManagedLinkAsync(Guid workspaceId, string shortCode, ClaimsPrincipal user,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<ManagedLink> GetWorkspaceManagedAnalyticsAsync(Guid workspaceId, string shortCode, ClaimsPrincipal user,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<LinkInsights> GetWorkspaceInsightsAsync(Guid workspaceId, string shortCode, string range, ClaimsPrincipal user,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<IReadOnlyList<LinkSparkline>> GetWorkspaceSparklinesAsync(Guid workspaceId, int days, ClaimsPrincipal user,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Uri PublicUrl(string shortCode);
}

public sealed class ShortenerApiClient : IShortenerApiClient
{
    private readonly HttpClient _http;
    private readonly IManagedApiTokenProvider _tokens;
    private readonly ILogger<ShortenerApiClient> _logger;
    private readonly Uri? _baseUri;
    private readonly Uri? _publicBaseUri;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ShortenerApiClient(HttpClient http, IManagedApiTokenProvider tokens,
        IConfiguration configuration, ILogger<ShortenerApiClient> logger)
    {
        _http = http;
        _tokens = tokens;
        _logger = logger;
        if (ShortenerApiAddress.TryResolve(configuration, out var uri, out _))
        {
            _baseUri = uri;
            _http.BaseAddress = _baseUri;
        }
        var publicConfigured = configuration["PublicLinks:BaseUrl"];
        if (Uri.TryCreate(publicConfigured, UriKind.Absolute, out var publicUri) &&
            (publicUri.Scheme == Uri.UriSchemeHttps || (publicUri.Scheme == Uri.UriSchemeHttp && publicUri.IsLoopback)) &&
            publicUri.AbsolutePath == "/" && string.IsNullOrEmpty(publicUri.UserInfo) &&
            string.IsNullOrEmpty(publicUri.Query) && string.IsNullOrEmpty(publicUri.Fragment))
            _publicBaseUri = new Uri(publicUri.GetLeftPart(UriPartial.Authority) + "/");
    }

    public Uri PublicUrl(string shortCode) => new(PublicBaseUri, "r/" + Uri.EscapeDataString(shortCode));

    public async Task<IReadOnlyList<WorkspaceSummary>> GetWorkspacesAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        using var message = await AuthorizedRequestAsync(HttpMethod.Get, "api/me/workspaces", user, cancellationToken);
        return await SendAsync<WorkspaceSummary[]>(message, "load workspaces", cancellationToken);
    }

    public async Task<LinkResult> CreateWorkspaceManagedAsync(Guid workspaceId, CreateLinkRequest request,
        ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        using var message = await AuthorizedRequestAsync(HttpMethod.Post, WorkspacePath(workspaceId), user, cancellationToken);
        message.Content = JsonContent.Create(request);
        var (link, status) = await SendWithStatusAsync<CreatedLink>(message, "create workspace link", cancellationToken);
        return new(link, PublicUrl(link.ShortCode), AlreadyExists: status == HttpStatusCode.OK);
    }

    public async Task<IReadOnlyList<ManagedLink>> GetWorkspaceManagedLinksAsync(Guid workspaceId,
        ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        using var message = await AuthorizedRequestAsync(HttpMethod.Get, WorkspacePath(workspaceId) + "/analytics", user, cancellationToken);
        return await SendAsync<ManagedLink[]>(message, "load workspace links", cancellationToken);
    }

    public async Task<ManagedLink> GetWorkspaceManagedLinkAsync(Guid workspaceId, string shortCode,
        ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        using var message = await AuthorizedRequestAsync(HttpMethod.Get, WorkspacePath(workspaceId) + "/" + Uri.EscapeDataString(shortCode), user, cancellationToken);
        return await SendAsync<ManagedLink>(message, "load workspace link", cancellationToken);
    }

    public async Task<ManagedLink> GetWorkspaceManagedAnalyticsAsync(Guid workspaceId, string shortCode,
        ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        using var message = await AuthorizedRequestAsync(HttpMethod.Get, WorkspacePath(workspaceId) + "/" + Uri.EscapeDataString(shortCode) + "/analytics", user, cancellationToken);
        return await SendAsync<ManagedLink>(message, "load workspace analytics", cancellationToken);
    }

    public async Task<LinkInsights> GetWorkspaceInsightsAsync(Guid workspaceId, string shortCode, string range,
        ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        using var message = await AuthorizedRequestAsync(HttpMethod.Get,
            WorkspacePath(workspaceId) + "/" + Uri.EscapeDataString(shortCode) + "/insights?range=" + Uri.EscapeDataString(range), user, cancellationToken);
        return await SendAsync<LinkInsights>(message, "load link insights", cancellationToken);
    }

    public async Task<IReadOnlyList<LinkSparkline>> GetWorkspaceSparklinesAsync(Guid workspaceId, int days,
        ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        using var message = await AuthorizedRequestAsync(HttpMethod.Get,
            WorkspacePath(workspaceId) + "/sparklines?days=" + days, user, cancellationToken);
        return await SendAsync<LinkSparkline[]>(message, "load link sparklines", cancellationToken);
    }

    public async Task<LinkResult> CreateGuestAsync(CreateLinkRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/urls/guest")
        { Content = JsonContent.Create(request) };
        var link = await SendAsync<CreatedLink>(message, "create guest link", cancellationToken);
        return new(link, PublicUrl(link.ShortCode));
    }

    public async Task<LinkResult> CreateManagedAsync(CreateLinkRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        using var message = await AuthorizedRequestAsync(HttpMethod.Post, "api/urls/managed", user, cancellationToken);
        message.Content = JsonContent.Create(request);
        var (link, status) = await SendWithStatusAsync<CreatedLink>(message, "create managed link", cancellationToken);
        return new(link, PublicUrl(link.ShortCode), AlreadyExists: status == HttpStatusCode.OK);
    }

    public async Task<IReadOnlyList<ManagedLink>> GetManagedLinksAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        using var message = await AuthorizedRequestAsync(HttpMethod.Get, "api/urls/managed/analytics", user, cancellationToken);
        return await SendAsync<ManagedLink[]>(message, "load managed links", cancellationToken);
    }

    public async Task<ManagedLink> GetManagedLinkAsync(string shortCode, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        using var message = await AuthorizedRequestAsync(HttpMethod.Get, ManagedPath(shortCode), user, cancellationToken);
        return await SendAsync<ManagedLink>(message, "load managed link", cancellationToken);
    }

    public async Task<ManagedLink> GetManagedAnalyticsAsync(string shortCode, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        using var message = await AuthorizedRequestAsync(HttpMethod.Get, ManagedPath(shortCode) + "/analytics", user, cancellationToken);
        return await SendAsync<ManagedLink>(message, "load analytics", cancellationToken);
    }

    private Uri BaseUri => _baseUri ?? throw new ShortenerApiException("The shortener API address is not configured.");
    private Uri PublicBaseUri => _publicBaseUri ?? throw new ShortenerApiException("The public link address is not configured.");

    private static string ManagedPath(string shortCode) => "api/urls/managed/" + Uri.EscapeDataString(shortCode);
    private static string WorkspacePath(Guid workspaceId) => "api/workspaces/" + workspaceId + "/urls/managed";

    private async Task<HttpRequestMessage> AuthorizedRequestAsync(HttpMethod method, string path,
        ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        _ = BaseUri;
        var token = await _tokens.GetTokenAsync(user, cancellationToken);
        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return message;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage message, string operation, CancellationToken cancellationToken)
        => (await SendWithStatusAsync<T>(message, operation, cancellationToken)).Value;

    private async Task<(T Value, HttpStatusCode Status)> SendWithStatusAsync<T>(HttpRequestMessage message, string operation, CancellationToken cancellationToken)
    {
        _ = BaseUri;
        try
        {
            using var response = await _http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Shortener API rejected {Operation}. StatusCode={StatusCode} CorrelationId={CorrelationId}",
                    operation, (int)response.StatusCode, System.Diagnostics.Activity.Current?.Id);
                throw new ShortenerApiException(response.StatusCode switch
                {
                    HttpStatusCode.BadRequest => "Check the destination URL (it must start with http:// or https://) and the custom code (4–12 letters or numbers, no spaces or symbols).",
                    HttpStatusCode.Conflict => "That custom code is already in use. Choose another.",
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Your managed-link access was denied. Sign in again or contact an administrator.",
                    HttpStatusCode.NotFound => "That managed link was not found in your tenant.",
                    _ => "The shortener API could not complete the request. Try again later."
                });
            }
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return (value ?? throw new JsonException("Empty API response"), response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Shortener API connection failed for {Operation}. ExceptionType={ExceptionType} CorrelationId={CorrelationId}",
                operation, ex.GetType().Name, System.Diagnostics.Activity.Current?.Id);
            throw new ShortenerApiException("The shortener API is unavailable. Try again later.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Shortener API response was invalid for {Operation}. ExceptionType={ExceptionType} CorrelationId={CorrelationId}",
                operation, ex.GetType().Name, System.Diagnostics.Activity.Current?.Id);
            throw new ShortenerApiException("The shortener API returned an invalid response. Try again later.");
        }
    }
}

public sealed class ShortenerApiException(string message) : Exception(message);
