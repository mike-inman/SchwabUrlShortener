using Shortener.Domain;

namespace Shortener.UnitTests;

public class UrlValidationTests
{
    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("http://example.com/path?q=1", "http://example.com/path?q=1")]
    public void NormalizeUrl_AddsScheme_WhenMissing(string input, string expected)
    {
        var result = UrlValidation.NormalizeUrl(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/path", true)]
    [InlineData("example.com", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("", false)]
    public void IsValidAbsoluteUrl_ReturnsExpectedValue(string input, bool expected)
    {
        var normalized = UrlValidation.NormalizeUrl(input);

        Assert.Equal(expected, UrlValidation.IsValidAbsoluteUrl(normalized));
    }

    [Theory]
    [InlineData("abcd", true)]
    [InlineData("AbC123", true)]
    [InlineData("abc12345", true)]
    [InlineData("abc", false)]
    [InlineData("abc1234567890", false)]
    [InlineData("abc-123", false)]
    public void IsValidShortCode_ReturnsExpectedValue(string input, bool expected)
    {
        Assert.Equal(expected, UrlValidation.IsValidShortCode(input));
    }

    [Fact]
    public void UrlMapping_Create_RecordsNormalizedUrlAndZeroHits()
    {
        var mapping = UrlMapping.Create("example.com", "Demo1");

        Assert.Equal("https://example.com", mapping.OriginalUrl);
        Assert.Equal("Demo1", mapping.ShortCode);
        Assert.Equal(0, mapping.HitCount);
        Assert.Null(mapping.LastAccessedAtUtc);
    }

    [Fact]
    public void UrlMapping_RecordHit_UpdatesAnalytics()
    {
        var mapping = UrlMapping.Create("https://example.com", "Demo1");

        mapping.RecordHit(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero));

        Assert.Equal(1, mapping.HitCount);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), mapping.LastAccessedAtUtc);
    }
}
