using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Shortener.Application;

namespace Shortener.Shared;

public static class HardeningHostConfiguration
{
    public const string GuestCreatePolicy = "guest-create";
    public const string RedirectPolicy = "redirect";
    public const string ManagedPolicy = "managed";
    public const string ContactPolicy = "contact";
    private static readonly Meter Meter = new("Shortener.RateLimiting");
    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>("shortener.rate_limit.rejections");
    private static readonly ConcurrentDictionary<string, long> LoggedWindows = new();

    public static void AddConfiguredRateLimiting(IServiceCollection services, IConfiguration configuration, bool web = false)
    {
        var enabled = ReadBool(configuration, "RateLimiting:Enabled");
        var guestLimit = ReadPositive(configuration, "RateLimiting:GuestCreate:PermitLimit");
        var guestWindow = ReadPositive(configuration, "RateLimiting:GuestCreate:WindowSeconds");
        var redirectBurst = ReadPositive(configuration, "RateLimiting:Redirect:TokenLimit");
        var redirectTokens = ReadPositive(configuration, "RateLimiting:Redirect:TokensPerPeriod");
        var redirectPeriod = ReadPositive(configuration, "RateLimiting:Redirect:ReplenishmentSeconds");
        if (redirectTokens > redirectBurst)
            throw new InvalidOperationException("RateLimiting:Redirect:TokensPerPeriod cannot exceed TokenLimit.");
        var managedLimit = ReadPositive(configuration, "RateLimiting:Managed:PermitLimit");
        var managedWindow = ReadPositive(configuration, "RateLimiting:Managed:WindowSeconds");
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            if (web)
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    enabled && HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/"
                        ? RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = guestLimit, Window = TimeSpan.FromSeconds(guestWindow), QueueLimit = 0,
                            AutoReplenishment = true
                        })
                        : enabled && HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/contact"
                        ? RateLimitPartition.GetFixedWindowLimiter("contact:" + ClientKey(context), _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true
                        })
                        : RateLimitPartition.GetNoLimiter("other"));
            options.AddPolicy(GuestCreatePolicy, context => enabled
                ? RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = guestLimit, Window = TimeSpan.FromSeconds(guestWindow), QueueLimit = 0,
                    AutoReplenishment = true
                })
                : RateLimitPartition.GetNoLimiter(ClientKey(context)));
            options.AddPolicy(RedirectPolicy, context => enabled
                ? RateLimitPartition.GetTokenBucketLimiter(ClientKey(context), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = redirectBurst, TokensPerPeriod = redirectTokens,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(redirectPeriod), QueueLimit = 0,
                    AutoReplenishment = true
                })
                : RateLimitPartition.GetNoLimiter(ClientKey(context)));
            options.AddPolicy(ManagedPolicy, context => enabled
                ? RateLimitPartition.GetFixedWindowLimiter(ManagedKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = managedLimit, Window = TimeSpan.FromSeconds(managedWindow), QueueLimit = 0,
                    AutoReplenishment = true
                })
                : RateLimitPartition.GetNoLimiter(ManagedKey(context)));
            options.AddPolicy(ContactPolicy, context => enabled
                ? RateLimitPartition.GetFixedWindowLimiter("contact:" + ClientKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true
                })
                : RateLimitPartition.GetNoLimiter("contact:" + ClientKey(context)));
            options.OnRejected = async (rejected, token) =>
            {
                var context = rejected.HttpContext;
                var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
                    ?? (web && HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/" ? GuestCreatePolicy :
                        web && HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/contact" ? ContactPolicy : "unknown");
                var key = policy == ManagedPolicy ? ManagedKey(context) : ClientKey(context);
                var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
                var window = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
                var logKey = policy + ":" + digest + ":" + window;
                Rejections.Add(1, new KeyValuePair<string, object?>("policy", policy));
                if (policy == ContactPolicy)
                    ContactMetrics.Rejected.Add(1, new KeyValuePair<string, object?>("reason", "rate_limit"));
                var retry = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan after)
                    ? Math.Max(1, (int)Math.Ceiling(after.TotalSeconds)) : 60;
                context.Response.Headers["Retry-After"] = retry.ToString(CultureInfo.InvariantCulture);
                if (LoggedWindows.TryAdd(logKey, window))
                {
                    // Duplicate requests within one minute do not repeat the log entry.
                    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimiting");
                    logger.LogWarning("Rate limit rejected. Policy={Policy} ClientHash={ClientHash} RetryAfterSeconds={RetryAfterSeconds}",
                        policy, digest, retry);
                }
                if (LoggedWindows.Count > 4096)
                    foreach (var stale in LoggedWindows.Where(x => x.Value < window - 1)) LoggedWindows.TryRemove(stale.Key, out _);
                await context.Response.WriteAsync("Rate limit exceeded. Try again later.", token);
            };
        });
    }

    public static IApplicationBuilder UsePublicHostRedirect(this IApplicationBuilder app, IConfiguration configuration)
    {
        var publicBase = configuration["PublicLinks:BaseUrl"];
        if (!Uri.TryCreate(publicBase, UriKind.Absolute, out var publicUri) ||
            (publicUri.Scheme != Uri.UriSchemeHttps &&
             !(publicUri.Scheme == Uri.UriSchemeHttp &&
               (publicUri.Host == "localhost" || System.Net.IPAddress.TryParse(publicUri.Host, out var address) &&
                System.Net.IPAddress.IsLoopback(address)))) ||
            publicUri.AbsolutePath != "/" || !string.IsNullOrEmpty(publicUri.UserInfo) ||
            !string.IsNullOrEmpty(publicUri.Query) || !string.IsNullOrEmpty(publicUri.Fragment))
            throw new InvalidOperationException("PublicLinks:BaseUrl must be an HTTPS origin (or local HTTP loopback origin) without credentials, query, or fragment.");
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/health") &&
                string.Equals(context.Request.Host.Host, "www." + publicUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                context.Response.Headers.Location = "https://" + publicUri.Authority + context.Request.PathBase +
                    context.Request.Path + context.Request.QueryString;
                return;
            }
            await next(context);
        });
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool web, IConfiguration configuration)
    {
        var formOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "AzureAd:Authority", "AzureAd:Instance" })
            if (Uri.TryCreate(configuration[key], UriKind.Absolute, out var authority) && authority.Scheme == "https")
                formOrigins.Add(authority.GetLeftPart(UriPartial.Authority));
        var identityOrigins = formOrigins.Count == 0 ? "" : " " + string.Join(' ', formOrigins);
        return app.Use(async (context, next) =>
        {
            var nonce = web ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)) : null;
            if (nonce is not null) context.Items["CspNonce"] = nonce;
            var csp = web
                ? "default-src 'self'; script-src 'self' 'nonce-" + nonce + "'; " +
                  "style-src 'self' 'unsafe-inline'; " +
                  "font-src 'self'; img-src 'self' data:; " +
                  "connect-src 'self' wss:" + identityOrigins + "; frame-ancestors 'none'; base-uri 'self'; form-action 'self'" + identityOrigins
                : "frame-ancestors 'none'";
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["Content-Security-Policy"] = csp;
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = web ? "strict-origin-when-cross-origin" : "no-referrer";
                if (web) context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                return Task.CompletedTask;
            });
            await next(context);
        });
    }

    private static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown";

    public static string ClientKeyHash(HttpContext context) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ClientKey(context))));

    private static string ManagedKey(HttpContext context)
    {
        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        return context.User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(issuer) && !string.IsNullOrEmpty(subject)
            ? issuer.Length.ToString(CultureInfo.InvariantCulture) + ":" + issuer + subject : ClientKey(context);
    }

    private static bool ReadBool(IConfiguration configuration, string key) =>
        bool.TryParse(configuration[key], out var value) ? value : throw new InvalidOperationException($"{key} must be true or false.");

    private static int ReadPositive(IConfiguration configuration, string key) =>
        int.TryParse(configuration[key], out var value) && value > 0 ? value :
        throw new InvalidOperationException($"{key} must be a positive integer.");
}
