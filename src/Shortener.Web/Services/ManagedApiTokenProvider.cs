using System.Security.Claims;
using Microsoft.Identity.Web;

namespace Shortener.Web.Services;

public interface IManagedApiTokenProvider
{
    Task<string> GetTokenAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}

public sealed class EntraApiTokenProvider(
    ITokenAcquisition tokenAcquisition, IConfiguration configuration,
    ILogger<EntraApiTokenProvider> logger) : IManagedApiTokenProvider
{
    public Task<string> GetTokenAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true)
            throw new ManagedAccessUnavailableException("Sign in to manage links.");
        var scope = configuration["ShortenerApi:Scope"];
        if (string.IsNullOrWhiteSpace(scope))
            throw new ManagedAccessUnavailableException("The managed API scope is not configured.");
        return AcquireAsync();

        async Task<string> AcquireAsync()
        {
            try { return await tokenAcquisition.GetAccessTokenForUserAsync([scope], user: user); }
            catch (MicrosoftIdentityWebChallengeUserException ex)
            {
                logger.LogInformation("Managed API access requires renewed sign-in. ExceptionType={ExceptionType}",
                    ex.GetType().Name);
                throw new ManagedReauthenticationRequiredException();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Managed API token acquisition failed. ExceptionType={ExceptionType} CorrelationId={CorrelationId}",
                    ex.GetType().Name, System.Diagnostics.Activity.Current?.Id);
                throw new ManagedAccessUnavailableException("Unable to acquire managed API access. Sign in again or contact an administrator.");
            }
        }
    }
}

public sealed class UnconfiguredTokenProvider : IManagedApiTokenProvider
{
    public Task<string> GetTokenAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        throw new ManagedAccessUnavailableException("Managed sign-in is not configured.");
}

public class ManagedAccessUnavailableException(string message) : Exception(message);

public sealed class ManagedReauthenticationRequiredException() :
    ManagedAccessUnavailableException("Your sign-in needs to be renewed to access managed links.");
