using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace Shortener.Web.Services;

public sealed record ContactAdminInfo(bool IsContactAdmin, int UnhandledContactCount);
public sealed record ContactMessageItem(Guid Id, DateTimeOffset CreatedAtUtc, string Name, string Email,
    string? Subject, string Message, DateTimeOffset? NotifiedAtUtc, DateTimeOffset? FailedAtUtc,
    DateTimeOffset? HandledAtUtc);
public sealed record ContactMessagePage(IReadOnlyList<ContactMessageItem> Items, string? NextCursor,
    int UnhandledContactCount);

public interface IContactInboxApiClient
{
    Task<ContactAdminInfo> GetInfoAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<ContactMessagePage> GetPageAsync(ClaimsPrincipal user, string? cursor = null,
        CancellationToken cancellationToken = default);
    Task MarkHandledAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default);
}

public sealed class ContactInboxApiClient : IContactInboxApiClient
{
    private readonly HttpClient http;
    private readonly IManagedApiTokenProvider tokens;
    private readonly ILogger<ContactInboxApiClient> logger;
    private readonly bool configured;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ContactInboxApiClient(HttpClient http, IManagedApiTokenProvider tokens,
        IConfiguration configuration, ILogger<ContactInboxApiClient> logger)
    {
        this.http = http;
        this.tokens = tokens;
        this.logger = logger;
        configured = ShortenerApiAddress.TryResolve(configuration, out var address, out _);
        if (configured) http.BaseAddress = address;
    }

    public Task<ContactAdminInfo> GetInfoAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
        SendAsync<ContactAdminInfo>(HttpMethod.Get, "api/me", user, cancellationToken);

    public Task<ContactMessagePage> GetPageAsync(ClaimsPrincipal user, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<ContactMessagePage>(HttpMethod.Get, "api/admin/contact-messages?take=50" +
            (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor)), user, cancellationToken);

    public async Task MarkHandledAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default) =>
        _ = await SendAsync<JsonElement>(HttpMethod.Post, $"api/admin/contact-messages/{id:D}/handled", user, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!configured) throw new ContactInboxUnavailableException();
        var token = await tokens.GetTokenAsync(user, cancellationToken);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new ContactInboxForbiddenException();
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Contact inbox API rejected request. StatusCode={StatusCode} TraceId={TraceId}",
                    (int)response.StatusCode, System.Diagnostics.Activity.Current?.Id);
                throw new ContactInboxUnavailableException();
            }
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new JsonException("Empty contact inbox response.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Contact inbox API unavailable. ExceptionType={ExceptionType} TraceId={TraceId}",
                ex.GetType().Name, System.Diagnostics.Activity.Current?.Id);
            throw new ContactInboxUnavailableException();
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Contact inbox API response invalid. ExceptionType={ExceptionType} TraceId={TraceId}",
                ex.GetType().Name, System.Diagnostics.Activity.Current?.Id);
            throw new ContactInboxUnavailableException();
        }
    }
}

public sealed class ContactInboxUnavailableException() : Exception("Contact messages are unavailable. Try again later.");
public sealed class ContactInboxForbiddenException() : Exception("You do not have access to contact messages.");
