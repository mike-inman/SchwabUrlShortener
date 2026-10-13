namespace Shortener.Application.Interfaces;

// The caller must provide an issuer and subject from a successfully validated access token.
public interface IWorkspaceLinkAccess
{
    Task<bool> CanManageLinksAsync(string issuer, string subject, Guid workspaceId,
        CancellationToken cancellationToken = default);
}
