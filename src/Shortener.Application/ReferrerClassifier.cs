namespace Shortener.Application;

public sealed record ReferrerSignal(string ReferrerGroup, string? ReferrerHost);

// Normalizes only the Referer host to a fixed group and a registrable domain.
// Never retains path, query, or scheme. See ADR Decision 3 for the known limitation
// of the two-label reduction (it does not special-case multi-label public suffixes).
public static class ReferrerClassifier
{
    private static readonly HashSet<string> LinkedIn = ["linkedin.com", "lnkd.in"];
    private static readonly HashSet<string> X = ["x.com", "t.co", "twitter.com"];
    private static readonly HashSet<string> Facebook = ["facebook.com", "fb.com"];
    private static readonly HashSet<string> Instagram = ["instagram.com"];
    private static readonly HashSet<string> WebmailHosts =
        ["mail.google.com", "outlook.live.com", "outlook.office.com", "outlook.office365.com", "mail.yahoo.com"];

    public static ReferrerSignal Classify(string? refererUrl)
    {
        if (string.IsNullOrWhiteSpace(refererUrl)) return new("Direct", null);
        if (!Uri.TryCreate(refererUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new("Other", null);

        var host = uri.Host.ToLowerInvariant();
        if (WebmailHosts.Contains(host)) return new("Email", RegistrableDomain(host));

        var registrable = RegistrableDomain(host);
        var group = registrable switch
        {
            _ when LinkedIn.Contains(registrable) => "LinkedIn",
            _ when X.Contains(registrable) => "X",
            _ when Facebook.Contains(registrable) => "Facebook",
            _ when Instagram.Contains(registrable) => "Instagram",
            _ when registrable.StartsWith("google.", StringComparison.Ordinal) || registrable == "google.com" => "Google",
            _ => "Other"
        };
        return new(group, registrable);
    }

    private static string RegistrableDomain(string host)
    {
        var labels = host.Split('.');
        return labels.Length <= 2 ? host : string.Join('.', labels[^2..]);
    }
}
