using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Shortener.Infrastructure;

public sealed record CustomerWorkspace(Guid Id, string Name, WorkspaceKind Kind);

public sealed class PostgresCustomerWorkspaces(IDbContextFactory<ShortenerDbContext> factory)
{
    public async Task<IReadOnlyList<CustomerWorkspace>> GetOrCreateAsync(string issuer, string subject,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("A validated external identity is required.");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var existing = await db.ExternalIdentities.AsNoTracking()
                .Where(x => x.Issuer == issuer && x.Subject == subject)
                .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(cancellationToken);
            if (existing is Guid userId)
            {
                var listed = await ListAsync(db, userId, cancellationToken);
                if (listed.Any(x => x.Kind == WorkspaceKind.Personal)) return listed;
                db.Workspaces.Add(new Workspace
                {
                    Id = Guid.NewGuid(), Kind = WorkspaceKind.Personal, PersonalOwnerUserId = userId,
                    Name = "Personal", CreatedAtUtc = DateTimeOffset.UtcNow
                });
                try { await db.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                    { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_workspaces_personal_owner" })
                {
                    // A concurrent call supplied the missing personal workspace.
                }
                await using var fresh = await factory.CreateDbContextAsync(cancellationToken);
                return await ListAsync(fresh, userId, cancellationToken);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            userId = Guid.NewGuid();
            var workspaceId = Guid.NewGuid();
            db.ApplicationUsers.Add(new ApplicationUser { Id = userId, CreatedAtUtc = now });
            db.ExternalIdentities.Add(new ExternalIdentity
            {
                Id = Guid.NewGuid(), UserId = userId, Issuer = issuer, Subject = subject, CreatedAtUtc = now
            });
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId, Kind = WorkspaceKind.Personal, PersonalOwnerUserId = userId,
                Name = "Personal", CreatedAtUtc = now
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return [new CustomerWorkspace(workspaceId, "Personal", WorkspaceKind.Personal)];
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_external_identities_issuer_subject" })
            {
                await transaction.RollbackAsync(cancellationToken);
                // Another request committed this identity. A fresh context sees its workspace.
            }
        }
        throw new InvalidOperationException("Unable to resolve the customer workspace after concurrent provisioning.");
    }

    private static async Task<IReadOnlyList<CustomerWorkspace>> ListAsync(ShortenerDbContext db,
        Guid userId, CancellationToken cancellationToken)
    {
        var personal = await db.Workspaces.AsNoTracking()
            .Where(x => x.PersonalOwnerUserId == userId)
            .Select(x => new CustomerWorkspace(x.Id, x.Name, x.Kind))
            .ToListAsync(cancellationToken);
        var teams = await (from member in db.WorkspaceMemberships.AsNoTracking()
            join workspace in db.Workspaces.AsNoTracking() on member.WorkspaceId equals workspace.Id
            where member.UserId == userId && workspace.Kind == WorkspaceKind.Team
            select new CustomerWorkspace(workspace.Id, workspace.Name, workspace.Kind))
            .ToListAsync(cancellationToken);
        return [.. personal, .. teams];
    }
}
