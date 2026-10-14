using System.Diagnostics;

namespace Shortener.Infrastructure;

internal static class RedisTelemetry
{
    public static readonly ActivitySource Source = new("Shortener.Redis");
}
