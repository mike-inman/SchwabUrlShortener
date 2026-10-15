using Shortener.Application;

namespace Shortener.UnitTests;

public sealed class ContactSubmissionTests
{
    [Fact]
    public void Required_fields_lengths_and_email_are_validated()
    {
        var invalid = ContactSubmissionValidator.Validate(new(null, "invalid", new string('s', 151), "short", null, null));
        Assert.Contains("Name", invalid.Keys);
        Assert.Contains("Email", invalid.Keys);
        Assert.Contains("Subject", invalid.Keys);
        Assert.Contains("Message", invalid.Keys);
        Assert.Empty(ContactSubmissionValidator.Validate(new("A visitor", "visitor@example.test", null,
            "A useful question for Ziplio", null, null)));
    }
}
