using System.ComponentModel.DataAnnotations;

namespace Shortener.Application;

public sealed record ContactSubmission(string? Name, string? Email, string? Subject, string? Message,
    string? Website, string? FormToken);

public static class ContactSubmissionValidator
{
    public static Dictionary<string, string[]> Validate(ContactSubmission submission)
    {
        var errors = new Dictionary<string, string[]>();
        var name = submission.Name?.Trim();
        var email = submission.Email?.Trim();
        var subject = submission.Subject?.Trim();
        var message = submission.Message?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            errors[nameof(submission.Name)] = ["Enter a name of at most 100 characters."];
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            errors[nameof(submission.Email)] = ["Enter a valid email address of at most 254 characters."];
        if (subject?.Length > 150)
            errors[nameof(submission.Subject)] = ["Subject must be at most 150 characters."];
        if (string.IsNullOrWhiteSpace(message) || message.Length is < 10 or > 4000)
            errors[nameof(submission.Message)] = ["Message must be between 10 and 4,000 characters."];
        return errors;
    }
}
