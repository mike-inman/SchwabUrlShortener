namespace Shortener.Application;

public static class AcceptLanguageParser
{
    // The primary tag only (for example "en-US"); no weighted negotiation is needed for display.
    public static string? ParsePrimary(string? acceptLanguageHeader)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguageHeader)) return null;
        var first = acceptLanguageHeader.Split(',', 2)[0];
        var tag = first.Split(';', 2)[0].Trim();
        return tag.Length is > 0 and <= 35 && tag.All(c =>
            c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-')
            ? tag : null;
    }
}
