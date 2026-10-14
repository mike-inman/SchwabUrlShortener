namespace Shortener.Application;

public static class AcceptLanguageParser
{
    // The primary tag only (for example "en-US"); no weighted negotiation is needed for display.
    public static string? ParsePrimary(string? acceptLanguageHeader)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguageHeader)) return null;
        var first = acceptLanguageHeader.Split(',', 2)[0];
        var tag = first.Split(';', 2)[0].Trim();
        return tag.Length == 0 ? null : tag;
    }
}
