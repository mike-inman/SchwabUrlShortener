using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Shortener.Web.Services;
using Xunit;

namespace Shortener.WebTests;

public sealed class ManagedApiTokenProviderTests
{
    [Fact]
    public async Task Customer_token_request_uses_configured_scope_and_oidc_scheme()
    {
        const string scope = "api://customer-test/access_as_user";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ShortenerApi:Scope"] = scope }).Build();
        var acquisition = DispatchProxy.Create<ITokenAcquisition, TokenAcquisitionProbe>();
        var probe = (TokenAcquisitionProbe)(object)acquisition;
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test-subject")], "test"));
        var provider = new EntraApiTokenProvider(acquisition, configuration,
            NullLogger<EntraApiTokenProvider>.Instance);

        Assert.Equal("test-access-token", await provider.GetTokenAsync(user, CancellationToken.None));
        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, probe.Scheme);
        Assert.Equal([scope], Assert.IsType<string[]>(probe.Scopes));
        Assert.Same(user, probe.User);
    }

    [Fact]
    public async Task Empty_token_cache_requires_interactive_renewal()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ShortenerApi:Scope"] = "api://customer-test/access_as_user" }).Build();
        var acquisition = DispatchProxy.Create<ITokenAcquisition, TokenAcquisitionProbe>();
        ((TokenAcquisitionProbe)(object)acquisition).Failure =
            new MsalUiRequiredException("test-cache-empty", "Test cache is empty.");
        var provider = new EntraApiTokenProvider(acquisition, configuration,
            NullLogger<EntraApiTokenProvider>.Instance);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test-subject")], "test"));

        await Assert.ThrowsAsync<ManagedReauthenticationRequiredException>(() =>
            provider.GetTokenAsync(user, CancellationToken.None));
    }

    public class TokenAcquisitionProbe : DispatchProxy
    {
        public string? Scheme { get; private set; }
        public string[]? Scopes { get; private set; }
        public ClaimsPrincipal? User { get; private set; }
        public Exception? Failure { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(ITokenAcquisition.GetAccessTokenForUserAsync) || args is null)
                throw new NotSupportedException(targetMethod?.Name);
            var parameters = targetMethod.GetParameters();
            Scheme = (string?)args[Array.FindIndex(parameters, item => item.Name == "authenticationScheme")];
            Scopes = ((IEnumerable<string>)args[Array.FindIndex(parameters, item => item.Name == "scopes")]!).ToArray();
            User = (ClaimsPrincipal?)args[Array.FindIndex(parameters, item => item.Name == "user")];
            if (Failure is not null) throw Failure;
            return Task.FromResult("test-access-token");
        }
    }
}
