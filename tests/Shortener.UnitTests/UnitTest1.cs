using Shortener.Application;
using Shortener.Application.Interfaces;
using Shortener.Domain;

namespace Shortener.UnitTests;

public class UrlValidationTests
{
    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("http://example.com/path?q=1", "http://example.com/path?q=1")]
    public void NormalizeUrl_AddsScheme_WhenMissing(string input, string expected)
        => Assert.Equal(expected, UrlValidation.NormalizeUrl(input));

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/path", true)]
    [InlineData("example.com", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("", false)]
    public void IsValidAbsoluteUrl_ReturnsExpectedValue(string input, bool expected)
        => Assert.Equal(expected, UrlValidation.IsValidAbsoluteUrl(UrlValidation.NormalizeUrl(input)));

    [Theory]
    [InlineData("abcd", true)]
    [InlineData("AbC123", true)]
    [InlineData("abc12345", true)]
    [InlineData("abc", false)]
    [InlineData("abc1234567890", false)]
    [InlineData("abc-123", false)]
    [InlineData("äbcd", false)]
    public void IsValidShortCode_ReturnsExpectedValue(string input, bool expected)
        => Assert.Equal(expected, UrlValidation.IsValidShortCode(input));

    [Fact]
    public void GuestExpiresExactlySevenDaysAndCannotCollectAnalytics()
    {
        var now = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        var guest = UrlMapping.CreateGuest("example.com", "Demo1", now);
        Assert.Equal(LinkMode.Guest, guest.Mode);
        Assert.Null(guest.TenantId);
        Assert.Equal(now.AddDays(7), guest.ExpiresAtUtc);
        Assert.False(guest.IsExpired(now.AddDays(7).AddTicks(-1)));
        Assert.True(guest.IsExpired(now.AddDays(7)));
        Assert.Throws<InvalidOperationException>(() => guest.RecordManagedHit(now));
        Assert.Equal(0, guest.HitCount);
        Assert.Null(guest.LastAccessedAtUtc);
    }

    [Fact]
    public void ManagedRequiresTenantAndHasNoExpiry()
    {
        var now = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => UrlMapping.CreateManaged("example.com", "Demo1", Guid.Empty, now));
        var tenant = Guid.NewGuid();
        var managed = UrlMapping.CreateManaged("example.com", "Demo1", tenant, now);
        Assert.Equal(tenant, managed.TenantId);
        Assert.Null(managed.ExpiresAtUtc);
        managed.RecordManagedHit(now.AddMinutes(1));
        Assert.Equal(1, managed.HitCount);
    }
}

public class UrlShortenerServiceTests
{
    [Fact]
    public async Task ManagedCreationAllowsRepeatedDestinationsAndCodesRemainGlobal()
    {
        var repo = new TestRepository();
        var service = new UrlShortenerService(repo, TimeProvider.System);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var first = await service.CreateManagedAsync(a, "example.com", "code123");
        var second = await service.CreateManagedAsync(b, "example.com", "code456");
        Assert.NotEqual(first.Id, second.Id);
        var repeatedCode = await service.CreateManagedWithOutcomeAsync(a, "example.com", "code123");
        Assert.True(repeatedCode.AlreadyExists);
        Assert.Equal(first.Id, repeatedCode.Mapping.Id);
        var generatedOne = await service.CreateManagedWithOutcomeAsync(a, "example.com");
        var generatedTwo = await service.CreateManagedWithOutcomeAsync(a, "example.com");
        Assert.False(generatedOne.AlreadyExists);
        Assert.False(generatedTwo.AlreadyExists);
        Assert.NotEqual(first.ShortCode, generatedOne.Mapping.ShortCode);
        Assert.NotEqual(generatedOne.Mapping.ShortCode, generatedTwo.Mapping.ShortCode);
        await Assert.ThrowsAsync<ShortCodeConflictException>(() => service.CreateManagedAsync(a, "other.example", "code123"));
        await Assert.ThrowsAsync<ShortCodeConflictException>(() => service.CreateGuestAsync("other.example", "code123"));
        Assert.Null(await service.GetManagedAsync(b, "code123"));
        Assert.Single(await service.GetManagedAnalyticsAsync(b));
    }

    [Fact]
    public async Task GuestResolutionNeverRecordsAnalytics()
    {
        var repo = new TestRepository();
        var service = new UrlShortenerService(repo, TimeProvider.System);
        var guest = await service.CreateGuestAsync("example.com", "guest123");
        Assert.Same(guest, await service.ResolveAsync("guest123"));
        Assert.Equal(0, guest.HitCount);
        Assert.Null(guest.LastAccessedAtUtc);
    }

    private sealed class TestRepository : IUrlShortenerRepository
    {
        private readonly Dictionary<string, UrlMapping> _items = new(StringComparer.OrdinalIgnoreCase);
        public Task<bool> TryAddAsync(UrlMapping item, CancellationToken cancellationToken = default) => Task.FromResult(_items.TryAdd(item.ShortCode, item));
        public Task<UrlMapping?> GetByShortCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(code));
        public Task<UrlMapping?> GetManagedByShortCodeAsync(Guid tenant, string code, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(code) is { Mode: LinkMode.Managed } m && m.TenantId == tenant ? m : null);
        public Task<UrlMapping?> GetManagedByOriginalUrlAsync(Guid tenant, string url, CancellationToken cancellationToken = default) => Task.FromResult(_items.Values.FirstOrDefault(x => x.Mode == LinkMode.Managed && x.TenantId == tenant && x.OriginalUrl == url));
        public Task<IReadOnlyCollection<UrlMapping>> GetManagedByTenantAsync(Guid tenant, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<UrlMapping>>(_items.Values.Where(x => x.Mode == LinkMode.Managed && x.TenantId == tenant).ToArray());
        public Task RecordManagedHitAsync(UrlMapping mapping, DateTimeOffset now, CancellationToken cancellationToken = default) { mapping.RecordManagedHit(now); return Task.CompletedTask; }
    }
}
