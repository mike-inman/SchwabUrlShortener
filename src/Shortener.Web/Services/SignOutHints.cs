using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Shortener.Web.Services;

public static class SignOutHints
{
    private static readonly object RequestKey = new();

    public static void Capture(HttpContext context)
    {
        var hint = context.User.FindFirst("login_hint")?.Value;
        if (!string.IsNullOrWhiteSpace(hint))
            context.Items[RequestKey] = hint;
    }

    public static void Apply(HttpContext context, OpenIdConnectMessage message)
    {
        var hint = context.User.FindFirst("login_hint")?.Value ?? context.Items[RequestKey] as string;
        if (!string.IsNullOrWhiteSpace(hint))
            message.SetParameter("logout_hint", hint);
    }

    public static void Apply(ClaimsPrincipal user, OpenIdConnectMessage message)
    {
        var hint = user.FindFirst("login_hint")?.Value;
        if (!string.IsNullOrWhiteSpace(hint))
            message.SetParameter("logout_hint", hint);
    }
}
