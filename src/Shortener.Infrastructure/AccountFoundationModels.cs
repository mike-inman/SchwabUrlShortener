namespace Shortener.Infrastructure;

// Persistence-only account records. Authentication and workspace authorization are later slices.
public sealed class ApplicationUser
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? DisplayName { get; set; }
    public string? ContactEmail { get; set; }
}

public sealed class ExternalIdentity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public enum WorkspaceKind : short { Personal = 0, Team = 1 }
public enum TeamRole : short { Owner = 0, Admin = 1, Member = 2 }

public sealed class Workspace
{
    public Guid Id { get; set; }
    public WorkspaceKind Kind { get; set; }
    public Guid? PersonalOwnerUserId { get; set; }
    public Guid? LegacyTenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class WorkspaceMembership
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public TeamRole Role { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class WorkspaceInvitation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string VerifierHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RedeemedAtUtc { get; set; }
    public Guid? RedeemedByUserId { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
