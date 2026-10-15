namespace Shortener.Application;

public static class ContactInboxHint
{
    // A private, application-wide admin scope; only admin-authorized UI code subscribes.
    public static readonly Guid ScopeId = Guid.Parse("d42863dd-b1ac-4d03-b87c-a6a4a67f80f4");
    public static LinkChange Change => new(LinkChangeScope.Admin, ScopeId, "contact");
}
