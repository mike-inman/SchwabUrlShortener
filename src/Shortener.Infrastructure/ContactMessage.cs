namespace Shortener.Infrastructure;

public sealed class ContactMessage
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Subject { get; set; }
    public string Message { get; set; } = "";
    public string ClientKeyHash { get; set; } = "";
    public DateTimeOffset? NotifiedAtUtc { get; set; }
    public DateTimeOffset? FailedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public int NotificationAttempts { get; set; }
    public string? LastNotificationErrorCode { get; set; }
    public DateTimeOffset? HandledAtUtc { get; set; }
}
