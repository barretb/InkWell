namespace InkWell.Domain.Abstractions;

/// <summary>
/// The kinds of failure the domain and application layers report. Deliberately small: callers
/// map these onto user-facing messages, so adding a code means adding a message everywhere.
/// </summary>
public enum DomainErrorCode
{
    /// <summary>No failure. Only ever seen on a successful result's uninitialised error slot.</summary>
    None = 0,

    /// <summary>The requested entity does not exist in the store.</summary>
    NotFound = 1,

    /// <summary>The supplied input violates a domain rule (length, range, or set membership).</summary>
    ValidationError = 2,
}

/// <summary>
/// A failure with a code callers can branch on and a message suitable for presenting to the writer.
/// </summary>
/// <param name="Code">The kind of failure.</param>
/// <param name="Message">A human-readable explanation.</param>
public sealed record DomainError(DomainErrorCode Code, string Message)
{
    /// <summary>Creates a <see cref="DomainErrorCode.NotFound"/> error.</summary>
    public static DomainError NotFound(string message) => new(DomainErrorCode.NotFound, message);

    /// <summary>Creates a <see cref="DomainErrorCode.ValidationError"/> error.</summary>
    public static DomainError Validation(string message) => new(DomainErrorCode.ValidationError, message);
}
