namespace Shortener.Application;

public sealed class ShortCodeConflictException : Exception
{
    public ShortCodeConflictException() : base("The short code is already in use.") { }
}
