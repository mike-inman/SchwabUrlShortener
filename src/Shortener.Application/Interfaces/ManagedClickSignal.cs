namespace Shortener.Application.Interfaces;

// Signals derived at redirect time for a workspace-owned managed click, carrying none of the
// raw inputs (IP, full user agent, full referrer URL) they were derived from.
public sealed record ManagedClickSignal(
    string VisitorHash, string ReferrerGroup, string? ReferrerHost,
    string DeviceClass, string? OsFamily, string? BrowserFamily, string? Language, bool IsBot);

// Raw request-side inputs available only at the API boundary. Never persisted or logged as-is.
public sealed record ClickRequestContext(string? RemoteIp, string? UserAgent, string? Referer, string? AcceptLanguage)
{
    public static readonly ClickRequestContext Empty = new(null, null, null, null);
}
