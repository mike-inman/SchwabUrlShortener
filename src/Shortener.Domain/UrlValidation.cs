namespace Shortener.Domain;

public static class UrlValidation
{
    public static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = $"https://{trimmed}";
        }

        return trimmed;
    }

    public static bool IsValidAbsoluteUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    public static bool IsValidShortCode(string shortCode)
    {
        if (string.IsNullOrWhiteSpace(shortCode))
        {
            return false;
        }

        var candidate = shortCode.Trim();

        return candidate.Length is >= 4 and <= 12 &&
               candidate.All(char.IsLetterOrDigit);
    }
}
