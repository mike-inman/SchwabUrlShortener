using System.Net;

namespace Shortener.Web.Services;

public static class ShortenerApiAddress
{
    public static bool TryResolve(IConfiguration configuration, out Uri? baseUri, out string rejectionReason)
    {
        baseUri = null;
        var configured = configuration["ShortenerApi:BaseUrl"];
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri))
        {
            rejectionReason = "MissingOrInvalidUrl";
            return false;
        }
        if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            rejectionReason = "UnexpectedUrlComponents";
            return false;
        }
        if (uri.Scheme != Uri.UriSchemeHttps &&
            (uri.Scheme != Uri.UriSchemeHttp ||
             !uri.IsLoopback && !IsAllowedInternalHost(uri.Host, configuration["ShortenerApi:AllowedInternalHttpHosts"])))
        {
            rejectionReason = "SchemeOrHttpHostNotAllowed";
            return false;
        }

        baseUri = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
        rejectionReason = string.Empty;
        return true;
    }

    private static bool IsAllowedInternalHost(string host, string? allowedHosts) =>
        Uri.CheckHostName(host) == UriHostNameType.Dns && !IPAddress.TryParse(host, out _) &&
        (allowedHosts?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(candidate => string.Equals(candidate, host, StringComparison.OrdinalIgnoreCase)) ?? false);
}
