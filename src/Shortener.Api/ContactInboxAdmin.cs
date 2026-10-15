using System.Security.Claims;

namespace Shortener.Api;

public sealed class ContactInboxAdmin
{
    public const string Policy = "ContactInboxAdmin";
    private readonly HashSet<(string Issuer, string Subject)> subjects;

    public ContactInboxAdmin(IConfiguration configuration)
    {
        subjects = [];
        foreach (var entry in configuration.GetSection("Admin:Subjects").GetChildren())
        {
            var value = entry.Value ?? string.Empty;
            var separator = value.LastIndexOf('|');
            var issuer = separator < 0 ? string.Empty : value[..separator];
            var subject = separator < 0 ? string.Empty : value[(separator + 1)..];
            if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                issuer.Length > 500 || string.IsNullOrWhiteSpace(subject) || subject.Length > 500)
                throw new InvalidOperationException("Admin:Subjects entries must be exact HTTPS issuer|subject pairs.");
            subjects.Add((issuer, subject));
        }
    }

    public bool IsAdmin(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || subjects.Count == 0) return false;
        var issuers = principal.FindAll("iss").ToArray();
        var claims = principal.FindAll("sub").ToArray();
        return issuers.Length == 1 && claims.Length == 1 &&
            subjects.Contains((issuers[0].Value, claims[0].Value));
    }
}
