using Microsoft.Extensions.Logging.Abstractions;
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
        var service = CreateService(repo, new TestCache());
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
        var service = CreateService(repo, new TestCache());
        var guest = await service.CreateGuestAsync("example.com", "guest123");
        Assert.Equal(guest.Id, (await service.ResolveAsync("guest123"))?.Id);
        Assert.Equal(0, guest.HitCount);
        Assert.Null(guest.LastAccessedAtUtc);
    }

    [Fact]
    public async Task CacheHitSkipsMappingReadButRecordsManagedHit()
    {
        var repo = new TestRepository();
        var cache = new TestCache();
        var service = CreateService(repo, cache);
        var tenant = Guid.NewGuid();
        var managed = await service.CreateManagedAsync(tenant, "example.com", "manage123");
        Assert.Equal(managed.Id, (await service.ResolveAsync("MANAGE123"))?.Id);
        Assert.Equal(0, repo.MappingReads);
        Assert.Equal(1, managed.HitCount);
        Assert.Equal(1, repo.ManagedHitWrites);
        Assert.Equal(1, cache.SetCount);
    }

    [Fact]
    public async Task MissAndCacheOutageFallBackToRepository()
    {
        var repo = new TestRepository();
        var cache = new TestCache();
        var service = CreateService(repo, cache);
        var mapping = UrlMapping.CreateGuest("example.com", "guest123", TimeProvider.System.GetUtcNow());
        Assert.True(await repo.TryAddAsync(mapping));
        Assert.Equal(mapping.Id, (await service.ResolveAsync("guest123"))?.Id);
        Assert.Equal(1, repo.MappingReads);
        Assert.Equal(1, cache.SetCount);
        cache.ThrowOnRead = true;
        Assert.Equal(mapping.Id, (await service.ResolveAsync("guest123"))?.Id);
        Assert.Equal(2, repo.MappingReads);
    }

    [Fact]
    public async Task GuestCacheHitExpiresAtAbsoluteBoundaryAndTtlIsCapped()
    {
        var repo = new TestRepository();
        var cache = new TestCache();
        var clock = new TestClock();
        var service = CreateService(repo, cache, clock);
        await service.CreateGuestAsync("example.com", "guest123");
        Assert.Equal(TimeSpan.FromHours(1), cache.LastTtl);
        clock.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromTicks(-1)));
        Assert.NotNull(await service.ResolveAsync("guest123"));
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Null(await service.ResolveAsync("guest123"));
        Assert.Equal(0, repo.MappingReads);
        Assert.Equal(0, repo.ManagedHitWrites);
    }

    [Fact]
    public async Task DuplicateAndFailedCreationDoNotPublishDifferentRedirect()
    {
        var repo = new TestRepository();
        var cache = new TestCache();
        var service = CreateService(repo, cache);
        var tenant = Guid.NewGuid();
        var first = await service.CreateManagedAsync(tenant, "example.com", "manage123");
        Assert.Equal(1, cache.SetCount);
        var repeated = await service.CreateManagedWithOutcomeAsync(tenant, "example.com", "MANAGE123");
        Assert.True(repeated.AlreadyExists);
        Assert.Equal(first.Id, repeated.Mapping.Id);
        Assert.Equal(1, cache.SetCount);
        await Assert.ThrowsAsync<ShortCodeConflictException>(() => service.CreateGuestAsync("other.example", "manage123"));
        Assert.Equal(1, cache.SetCount);
        Assert.Equal(first.Id, (await service.ResolveAsync("manage123"))?.Id);
    }

    [Fact]
    public async Task CacheWriteFailureAfterCommitDoesNotFailCreation()
    {
        var repo = new TestRepository();
        var cache = new TestCache { ThrowOnWrite = true };
        var service = CreateService(repo, cache);
        var created = await service.CreateGuestAsync("example.com", "guest123");
        Assert.Equal(created.Id, (await repo.GetByShortCodeAsync("guest123"))?.Id);
        Assert.Equal(created.Id, (await service.ResolveAsync("guest123"))?.Id);
    }

    private static UrlShortenerService CreateService(TestRepository repo, TestCache cache, TimeProvider? clock = null) =>
        new(repo, cache, clock ?? TimeProvider.System, NullLogger<UrlShortenerService>.Instance);

    private sealed class TestRepository : IUrlShortenerRepository
    {
        private readonly Dictionary<string, UrlMapping> _items = new(StringComparer.OrdinalIgnoreCase);
        public int MappingReads { get; private set; }
        public int ManagedHitWrites { get; private set; }
        public Task<bool> TryAddAsync(UrlMapping item, CancellationToken cancellationToken = default) => Task.FromResult(_items.TryAdd(item.ShortCode, item));
        public Task<UrlMapping?> GetByShortCodeAsync(string code, CancellationToken cancellationToken = default) { MappingReads++; return Task.FromResult(_items.GetValueOrDefault(code)); }
        public Task<UrlMapping?> GetManagedByShortCodeAsync(Guid tenant, string code, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(code) is { Mode: LinkMode.Managed } m && m.TenantId == tenant ? m : null);
        public Task<UrlMapping?> GetManagedByOriginalUrlAsync(Guid tenant, string url, CancellationToken cancellationToken = default) => Task.FromResult(_items.Values.FirstOrDefault(x => x.Mode == LinkMode.Managed && x.TenantId == tenant && x.OriginalUrl == url));
        public Task<IReadOnlyCollection<UrlMapping>> GetManagedByTenantAsync(Guid tenant, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<UrlMapping>>(_items.Values.Where(x => x.Mode == LinkMode.Managed && x.TenantId == tenant).ToArray());
        public Task RecordManagedHitAsync(string mappingId, Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var mapping = _items.Values.Single(x => x.Id == mappingId && x.TenantId == tenantId);
            mapping.RecordManagedHit(now);
            ManagedHitWrites++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestCache : IRedirectCache
    {
        private readonly Dictionary<string, RedirectTarget> _entries = new(StringComparer.OrdinalIgnoreCase);
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnWrite { get; set; }
        public int SetCount { get; private set; }
        public TimeSpan LastTtl { get; private set; }
        public Task<RedirectTarget?> GetAsync(string shortCode, CancellationToken cancellationToken = default)
        {
            if (ThrowOnRead) throw new TimeoutException();
            return Task.FromResult(_entries.GetValueOrDefault(shortCode));
        }
        public Task SetAsync(RedirectTarget target, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrite) throw new TimeoutException();
            _entries[target.ShortCode] = target;
            SetCount++;
            LastTtl = ttl;
            return Task.CompletedTask;
        }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(!ThrowOnRead);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}
