using Azure.Communication.Email;
using Shortener.Analytics;
using Shortener.Infrastructure;

namespace Shortener.IntegrationTests;

public sealed class ContactEmailMessageTests
{
    [Fact]
    public void Message_has_plain_text_reply_to_and_sanitized_subject()
    {
        var message = NewMessage("Hello\r\nBcc: other@example.test", "Visitor\r\nName");
        var email = AzureCommunicationContactNotifier.BuildMessage(message, "noreply@ziplio.io", "mike.inman@ziplio.io");

        Assert.Equal("noreply@ziplio.io", email.SenderAddress);
        Assert.Equal("Ziplio contact: HelloBcc: other@example.test", email.Content.Subject);
        Assert.Contains("Email: visitor@example.test", email.Content.PlainText);
        Assert.Null(email.Content.Html);
        var replyTo = Assert.Single(email.ReplyTo);
        Assert.Equal("visitor@example.test", replyTo.Address);
        Assert.Equal("VisitorName", replyTo.DisplayName);
    }

    [Fact]
    public void Missing_subject_and_long_subject_have_bounded_headers()
    {
        var missing = AzureCommunicationContactNotifier.BuildMessage(NewMessage(null), "noreply@ziplio.io", "mike.inman@ziplio.io");
        Assert.Equal("Ziplio contact: (no subject)", missing.Content.Subject);

        var longSubject = AzureCommunicationContactNotifier.BuildMessage(NewMessage(new string('x', 150)), "noreply@ziplio.io", "mike.inman@ziplio.io");
        Assert.Equal(120, longSubject.Content.Subject.Length);
        Assert.StartsWith("Ziplio contact: ", longSubject.Content.Subject);
    }

    [Fact]
    public void Failed_acs_status_is_a_retryable_delivery_failure()
    {
        var failure = Assert.Throws<ContactEmailDeliveryException>(() => AzureCommunicationContactNotifier.EnsureSucceeded(EmailSendStatus.Failed));
        Assert.Equal("EmailSendFailed", failure.Code);
        AzureCommunicationContactNotifier.EnsureSucceeded(EmailSendStatus.Succeeded);
    }

    private static ContactMessage NewMessage(string? subject, string name = "Visitor") => new()
    {
        Id = Guid.NewGuid(), Name = name, Email = "visitor@example.test", Subject = subject,
        Message = "A useful question for Ziplio."
    };
}
