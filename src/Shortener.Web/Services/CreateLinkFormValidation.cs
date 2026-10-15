using System.Net;

namespace Shortener.Web.Services;

public enum CreateLinkField { DestinationUrl, ShortCode }

public sealed record CreateLinkFormErrors(string? DestinationUrl = null, string? ShortCode = null)
{
    public bool HasErrors => DestinationUrl is not null || ShortCode is not null;
}

public static class CreateLinkFormValidation
{
    public const string UrlMessage = "Enter a full URL starting with http:// or https://";
    public const string CodeMessage = "Use 4–12 letters or numbers, with no spaces or symbols";
    public const string DuplicateMessage = "That code is already taken. Choose another.";

    public static CreateLinkFormErrors Validate(string? url, string? code)
    {
        var validUrl = Uri.TryCreate(url, UriKind.Absolute, out var address) &&
            address.Scheme is "http" or "https";
        var validCode = string.IsNullOrEmpty(code) ||
            (code.Length is >= 4 and <= 12 && code.All(c =>
                c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'));
        return new(validUrl ? null : UrlMessage, validCode ? null : CodeMessage);
    }

    public static CreateLinkFormErrors FromApi(ShortenerApiException exception) =>
        exception.StatusCode == HttpStatusCode.Conflict
            ? new(ShortCode: DuplicateMessage)
            : exception.StatusCode == HttpStatusCode.BadRequest
                ? exception.Field switch
                {
                    CreateLinkField.DestinationUrl => new(DestinationUrl: UrlMessage),
                    CreateLinkField.ShortCode => new(ShortCode: CodeMessage),
                    _ => new()
                }
                : new();
}
