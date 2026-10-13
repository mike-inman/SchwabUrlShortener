using Microsoft.EntityFrameworkCore;
using Shortener.Application.Interfaces;

namespace Shortener.Infrastructure;

public sealed class PostgresWorkspaceLinkAccess(IDbContextFactory<ShortenerDbContext> contextFactory) : IWorkspaceLinkAccess
{
    public async Task<bool> CanManageLinksAsync(string issuer, string subject, Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
            return false;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var userId = await db.ExternalIdentities.AsNoTracking()
            .Where(identity => identity.Issuer == issuer && identity.Subject == subject)
            .Select(identity => identity.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (userId == Guid.Empty) return false;

        return await db.Workspaces.AsNoTracking().AnyAsync(workspace =>
            workspace.Id == workspaceId &&
            (workspace.Kind == WorkspaceKind.Personal && workspace.PersonalOwnerUserId == userId ||
             workspace.Kind == WorkspaceKind.Team && db.WorkspaceMemberships.Any(membership =>
                 membership.WorkspaceId == workspace.Id && membership.UserId == userId)), cancellationToken);
    }
}
