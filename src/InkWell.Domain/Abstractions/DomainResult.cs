namespace InkWell.Domain.Abstractions;

/// <summary>
/// The outcome of an operation that either succeeds or fails with a <see cref="DomainError"/>.
/// Used instead of exceptions for expected failures (a missing chapter, an invalid title) so that
/// callers must deal with them and the UI can present them without a try/catch on every call.
/// </summary>
public readonly struct DomainResult : IEquatable<DomainResult>
{
    private readonly DomainError? _error;

    private DomainResult(DomainError? error) => _error = error;

    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess => _error is null;

    /// <summary>Whether the operation failed.</summary>
    public bool IsFailure => _error is not null;

    /// <summary>The failure. Throws when the result is a success.</summary>
    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public DomainError Error => _error
        ?? throw new InvalidOperationException("A successful result has no error.");

    /// <summary>A successful result.</summary>
    public static DomainResult Success() => new(null);

    /// <summary>A failed result carrying <paramref name="error"/>.</summary>
    public static DomainResult Failure(DomainError error) => new(error);

    /// <summary>A <see cref="DomainErrorCode.NotFound"/> failure.</summary>
    public static DomainResult NotFound(string message) => new(DomainError.NotFound(message));

    /// <summary>A <see cref="DomainErrorCode.ValidationError"/> failure.</summary>
    public static DomainResult Validation(string message) => new(DomainError.Validation(message));

    /// <inheritdoc />
    public bool Equals(DomainResult other) => Equals(_error, other._error);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DomainResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _error?.GetHashCode() ?? 0;

    /// <summary>Compares two results for equality.</summary>
    public static bool operator ==(DomainResult left, DomainResult right) => left.Equals(right);

    /// <summary>Compares two results for inequality.</summary>
    public static bool operator !=(DomainResult left, DomainResult right) => !left.Equals(right);
}

/// <summary>
/// The outcome of an operation that produces a value on success.
/// </summary>
/// <typeparam name="T">The produced value's type.</typeparam>
public readonly struct DomainResult<T> : IEquatable<DomainResult<T>>
{
    private readonly T? _value;
    private readonly DomainError? _error;

    private DomainResult(T? value, DomainError? error)
    {
        _value = value;
        _error = error;
    }

    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess => _error is null;

    /// <summary>Whether the operation failed.</summary>
    public bool IsFailure => _error is not null;

    /// <summary>The produced value. Throws when the result is a failure.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value => _error is null
        ? _value!
        : throw new InvalidOperationException($"A failed result has no value: {_error.Message}");

    /// <summary>The failure. Throws when the result is a success.</summary>
    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public DomainError Error => _error
        ?? throw new InvalidOperationException("A successful result has no error.");

    /// <summary>A successful result carrying <paramref name="value"/>.</summary>
    public static DomainResult<T> Success(T value) => new(value, null);

    /// <summary>A failed result carrying <paramref name="error"/>.</summary>
    public static DomainResult<T> Failure(DomainError error) => new(default, error);

    /// <summary>A <see cref="DomainErrorCode.NotFound"/> failure.</summary>
    public static DomainResult<T> NotFound(string message) => new(default, DomainError.NotFound(message));

    /// <summary>A <see cref="DomainErrorCode.ValidationError"/> failure.</summary>
    public static DomainResult<T> Validation(string message) => new(default, DomainError.Validation(message));

    /// <summary>Discards the value, keeping only success or failure.</summary>
    public DomainResult WithoutValue() => _error is null ? DomainResult.Success() : DomainResult.Failure(_error);

    /// <inheritdoc />
    public bool Equals(DomainResult<T> other)
        => Equals(_error, other._error) && EqualityComparer<T?>.Default.Equals(_value, other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DomainResult<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_value, _error);

    /// <summary>Compares two results for equality.</summary>
    public static bool operator ==(DomainResult<T> left, DomainResult<T> right) => left.Equals(right);

    /// <summary>Compares two results for inequality.</summary>
    public static bool operator !=(DomainResult<T> left, DomainResult<T> right) => !left.Equals(right);
}
