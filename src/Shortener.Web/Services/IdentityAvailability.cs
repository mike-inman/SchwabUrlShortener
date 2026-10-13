namespace Shortener.Web.Services;

public sealed record IdentityAvailability(bool Configured, bool Customer = false);
