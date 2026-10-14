namespace Shortener.Application;

public sealed record DeviceSignal(string DeviceClass, string? OsFamily, string? BrowserFamily, bool IsBot);

// A small, exhaustively tested rule set, chosen over a new NuGet dependency for Phase 1
// (see docs/2026-10-13-1500-link-insights-collection-adr.md, Decision 3). Only coarse
// families are derived; no version information is ever kept.
public static class UserAgentClassifier
{
    private static readonly string[] BotSignatures =
    [
        "bot", "spider", "crawler", "curl/", "wget", "python-requests", "python-urllib",
        "facebookexternalhit", "slackbot", "discordbot", "telegrambot", "whatsapp",
        "pingdom", "uptimerobot", "monitor", "headlesschrome", "phantomjs"
    ];

    public static DeviceSignal Classify(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return new("Unknown", null, null, false);
        var ua = userAgent.ToLowerInvariant();

        if (BotSignatures.Any(ua.Contains)) return new("Bot", null, null, true);

        var osFamily = ua switch
        {
            _ when ua.Contains("iphone") || ua.Contains("ipad") || ua.Contains("ipod") => "iOS",
            _ when ua.Contains("android") => "Android",
            _ when ua.Contains("windows") => "Windows",
            _ when ua.Contains("macintosh") || ua.Contains("mac os x") => "macOS",
            _ when ua.Contains("linux") => "Linux",
            _ => null
        };

        var browserFamily = ua switch
        {
            _ when ua.Contains("edg/") || ua.Contains("edge/") || ua.Contains("edga/") || ua.Contains("edgios/") => "Edge",
            _ when ua.Contains("crios") || ua.Contains("chrome/") => "Chrome",
            _ when ua.Contains("firefox/") || ua.Contains("fxios") => "Firefox",
            _ when ua.Contains("safari/") && !ua.Contains("chrome") && !ua.Contains("crios") => "Safari",
            _ => null
        };

        var deviceClass = ua switch
        {
            _ when ua.Contains("ipad") || (ua.Contains("android") && !ua.Contains("mobile")) => "Tablet",
            _ when ua.Contains("mobile") || ua.Contains("iphone") || ua.Contains("ipod") => "Mobile",
            _ when osFamily is "Windows" or "macOS" or "Linux" => "Desktop",
            _ => "Unknown"
        };

        return new(deviceClass, osFamily, browserFamily, false);
    }
}
