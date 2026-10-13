using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Shortener.Web.Components.Pages;
using Xunit;

namespace Shortener.WebTests;

public sealed class SignOutHttpTests
{
    [Fact]
    public async Task Sign_out_expires_every_cookie_chunk_and_fresh_request_is_anonymous()
    {
        using var factory = new SignOutFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        var authOptions = factory.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme,
            authOptions.DefaultAuthenticateScheme ?? authOptions.DefaultScheme);
        Assert.True(factory.Services.GetRequiredService<Shortener.Web.Services.IdentityAvailability>().Customer, "Identity mode is not Customer");
        var oidc = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("OpenIdConnect");
        Assert.Contains("api://test/access_as_user", oidc.Scope);
        Assert.NotEqual(new OpenIdConnectEvents().OnAuthorizationCodeReceived.Method,
            oidc.Events.OnAuthorizationCodeReceived.Method);

        using var login = await client.GetAsync("/__test/login");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginCookies = CookiePairs(login);
        var authNames = loginCookies.Select(Name).Where(name => name.StartsWith(".AspNetCore.Cookies", StringComparison.Ordinal)).ToArray();
        Assert.Contains(".AspNetCore.Cookies", authNames);
        Assert.Contains(authNames, name => name.EndsWith("C1", StringComparison.Ordinal));

        using var hintRequest = new HttpRequestMessage(HttpMethod.Get, "/__test/hint");
        hintRequest.Headers.Add("Cookie", string.Join("; ", loginCookies));
        using var hint = await client.SendAsync(hintRequest);
        Assert.Equal(HttpStatusCode.NoContent, hint.StatusCode);

        using var homeRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        homeRequest.Headers.Add("Cookie", string.Join("; ", loginCookies));
        using var home = await client.SendAsync(homeRequest);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var authenticatedHtml = await home.Content.ReadAsStringAsync();
        Assert.Contains("action=\"/account/signout\"", authenticatedHtml);
        Assert.Contains("no-store", home.Headers.CacheControl?.ToString());
        var tokenInput = Regex.Match(authenticatedHtml, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*>");
        Assert.True(tokenInput.Success);
        var token = Regex.Match(tokenInput.Value, "value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        using var signOutRequest = new HttpRequestMessage(HttpMethod.Post, "/account/signout")
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", token)])
        };
        signOutRequest.Headers.Add("Cookie", string.Join("; ", loginCookies.Concat(CookiePairs(home))));
        using var signOut = await client.SendAsync(signOutRequest);
        Assert.Equal(HttpStatusCode.Redirect, signOut.StatusCode);
        Assert.Equal(1, factory.PriorSignOutCalls);
        Assert.Contains("logout_hint=opaque-test-hint", signOut.Headers.Location?.Query);
        Assert.Contains("signout-callback-oidc", signOut.Headers.Location?.Query);
        var expired = SetCookies(signOut).ToArray();
        foreach (var name in authNames)
        {
            var deletion = Assert.Single(expired, cookie => Name(cookie) == name);
            Assert.True(deletion.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
                        deletion.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
        }

        using var fresh = await client.GetAsync("/");
        var anonymousHtml = await fresh.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/account/signin\"", anonymousHtml);
        Assert.DoesNotContain("action=\"/account/signout\"", anonymousHtml);
        using var signedOut = await client.GetAsync("/signed-out");
        Assert.Equal(HttpStatusCode.OK, signedOut.StatusCode);
        var signedOutHtml = await signedOut.Content.ReadAsStringAsync();
        Assert.Contains("You're signed out.", signedOutHtml);
        Assert.DoesNotContain("action=\"/account/signout\"", signedOutHtml);
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var headers) ? headers : [];

    private static IEnumerable<string> CookiePairs(HttpResponseMessage response) =>
        SetCookies(response).Select(cookie => cookie.Split(';', 2)[0]);

    private static string Name(string cookie) => cookie.Split('=', 2)[0];

    private sealed class SignOutFactory : WebApplicationFactory<SignedOut>
    {
        public int PriorSignOutCalls { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            foreach (var setting in new Dictionary<string, string>
            {
                ["Identity:Mode"] = "Customer",
                ["DataProtection:Mode"] = "Filesystem",
                ["DataProtection:ApplicationName"] = "Shortener.Web",
                ["TokenCache:Mode"] = "InMemory",
                ["AzureAd:ClientId"] = "11111111-1111-1111-1111-111111111111",
                ["AzureAd:TenantId"] = "22222222-2222-2222-2222-222222222222",
                ["AzureAd:Authority"] = "https://idp.example.test/tenant/v2.0",
                ["AzureAd:ClientSecret"] = "test-only-client-secret",
                ["ShortenerApi:Scope"] = "api://test/access_as_user",
                ["ShortenerApi:BaseUrl"] = "https://api.example.test"
            }) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureTestServices(services =>
            {
                services.AddTransient<IStartupFilter, TestSignInFilter>();
                services.Configure<OpenIdConnectOptions>("OpenIdConnect", options =>
                {
                    var previous = options.Events.OnRedirectToIdentityProviderForSignOut;
                    options.Events.OnRedirectToIdentityProviderForSignOut = async context =>
                    {
                        PriorSignOutCalls++;
                        await previous(context);
                    };
                });
                services.PostConfigure<OpenIdConnectOptions>("OpenIdConnect", options =>
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration { EndSessionEndpoint = "https://idp.example.test/logout" }));
            });
        }
    }

    private sealed class TestSignInFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, downstream) =>
            {
                if (context.Request.Path == "/__test/login")
                {
                    var claims = Enumerable.Range(0, 100)
                        .Select(index => new Claim("test-claim-" + index, Guid.NewGuid().ToString("N")))
                        .Append(new Claim("login_hint", "opaque-test-hint"));
                    await context.SignInAsync("Cookies", new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies")));
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    return;
                }
                if (context.Request.Path == "/__test/hint")
                {
                    var result = await context.AuthenticateAsync("Cookies");
                    context.Response.StatusCode = result.Principal?.HasClaim("login_hint", "opaque-test-hint") == true
                        ? StatusCodes.Status204NoContent
                        : StatusCodes.Status404NotFound;
                    return;
                }
                await downstream();
            });
            next(app);
        };
    }
}
