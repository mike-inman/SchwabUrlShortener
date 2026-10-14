using Shortener.Application;

namespace Shortener.UnitTests;

public sealed class LinkInsightsClassificationTests
{
    [Theory]
    [InlineData(null, "Direct", null)]
    [InlineData("https://www.linkedin.com/private/path?token=value", "LinkedIn", "linkedin.com")]
    [InlineData("https://mail.google.com/mail/u/0/#inbox", "Email", "google.com")]
    [InlineData("https://t.co/private", "X", "t.co")]
    [InlineData("https://www.google.com/search?q=secret", "Google", "google.com")]
    [InlineData("https://example.org/a/b?secret=1", "Other", "example.org")]
    [InlineData("javascript:alert(1)", "Other", null)]
    public void Referrer_has_only_coarse_group_and_host(string? input, string group, string? host)
    {
        var result = ReferrerClassifier.Classify(input);
        Assert.Equal(group, result.ReferrerGroup);
        Assert.Equal(host, result.ReferrerHost);
        Assert.DoesNotContain("/private", result.ToString());
        Assert.DoesNotContain("token=", result.ToString());
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Safari/604.1", "Mobile", "iOS", "Safari", false)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0 Edg/120.0", "Desktop", "Windows", "Edge", false)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Tablet) Chrome/120.0", "Tablet", "Android", "Chrome", false)]
    [InlineData("ExampleCrawlerBot/1.0", "Bot", null, null, true)]
    public void User_agent_is_reduced_to_coarse_families(string input, string device, string? os, string? browser, bool bot)
    {
        var result = UserAgentClassifier.Classify(input);
        Assert.Equal(device, result.DeviceClass);
        Assert.Equal(os, result.OsFamily);
        Assert.Equal(browser, result.BrowserFamily);
        Assert.Equal(bot, result.IsBot);
        Assert.DoesNotContain("120.0", result.ToString());
    }

    [Fact]
    public void Daily_hash_is_stable_within_day_and_unlinkable_across_days_and_links()
    {
        var service = VisitorHashService.FromConfiguredSecret("unit-test-visitor-secret-at-least-32-bytes");
        var day = new DateOnly(2026, 10, 14);
        var hash = service.Compute("192.0.2.64", "Browser/11.7", "mapping-a", day);
        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, service.Compute("192.0.2.64", "Browser/11.7", "mapping-a", day));
        Assert.NotEqual(hash, service.Compute("192.0.2.64", "Browser/11.7", "mapping-a", day.AddDays(1)));
        Assert.NotEqual(hash, service.Compute("192.0.2.64", "Browser/11.7", "mapping-b", day));
        Assert.DoesNotContain("192.0.2.64", hash);
        Assert.Throws<ArgumentException>(() => VisitorHashService.FromConfiguredSecret("short"));
    }

    [Fact]
    public void Primary_language_tag_excludes_weights_and_invalid_or_oversized_values()
    {
        Assert.Equal("en-US", AcceptLanguageParser.ParsePrimary("en-US,en;q=0.8"));
        Assert.Null(AcceptLanguageParser.ParsePrimary("en-US/secret"));
        Assert.Null(AcceptLanguageParser.ParsePrimary(new string('a', 36)));
    }
}
