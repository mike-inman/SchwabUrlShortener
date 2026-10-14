using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shortener.Application;

namespace Shortener.Web.Services;

public sealed class ContactApiClient(HttpClient http, IConfiguration configuration)
{
    private Uri BaseAddress => ShortenerApiAddress.TryResolve(configuration, out var address, out _)
        ? address! : throw new InvalidOperationException("Contact API is unavailable.");

    public async Task<string> GetFormTokenAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(new Uri(BaseAddress, "api/contact/form-token"), cancellationToken);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<FormToken>(cancellationToken);
        return token?.Token ?? throw new InvalidOperationException("Contact form token is unavailable.");
    }

    public async Task<Dictionary<string, string[]>?> SubmitAsync(ContactSubmission submission,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(new Uri(BaseAddress, "api/contact"), submission, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Created) return null;
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            using var content = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (content.RootElement.TryGetProperty("errors", out var errors))
                return errors.EnumerateObject().ToDictionary(x => x.Name,
                    x => x.Value.EnumerateArray().Select(value => value.GetString() ?? "Invalid value.").ToArray());
        }
        throw new HttpRequestException("Contact request failed.", null, response.StatusCode);
    }

    private sealed record FormToken(string Token);
}
