using System.Security.Cryptography;
using System.Text;

namespace Shortener.Application;

// Daily-rotating visitor hash. The "rotation" is a deterministic per-day key derived from
// one configured base secret, rather than a stored/rotated value - see ADR Decision 2.
// Never given raw IP or user agent beyond this one computation; only the resulting hash leaves it.
public sealed class VisitorHashService(byte[] baseSecret)
{
    public static VisitorHashService FromConfiguredSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new ArgumentException("A visitor-hash daily secret of at least 32 bytes is required.", nameof(secret));
        return new VisitorHashService(Encoding.UTF8.GetBytes(secret));
    }

    public string Compute(string? remoteIp, string? userAgent, string mappingId, DateOnly utcDate)
    {
        var dayKey = HMACSHA256.HashData(baseSecret, Encoding.UTF8.GetBytes(utcDate.ToString("yyyy-MM-dd")));
        var payload = Encoding.UTF8.GetBytes((remoteIp ?? string.Empty) + '\0' + (userAgent ?? string.Empty) + '\0' + mappingId);
        var full = HMACSHA256.HashData(dayKey, payload);
        return Convert.ToHexString(full.AsSpan(0, 16));
    }
}
