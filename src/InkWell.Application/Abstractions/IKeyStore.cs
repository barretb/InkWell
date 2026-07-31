namespace InkWell.Application.Abstractions;

/// <summary>
/// Holds the 256-bit key that encrypts the local database (FR-016). Implemented over the
/// platform's secure storage — Keychain, Android Keystore, or DPAPI — so the key never sits beside
/// the data it protects.
/// </summary>
public interface IKeyStore
{
    /// <summary>
    /// Returns the database key, generating and storing a new random one on first run.
    /// </summary>
    /// <exception cref="KeyStoreUnavailableException">
    /// Secure storage could not be reached — typically a missing Keychain entitlement on
    /// iOS/Mac Catalyst (research.md §2).
    /// </exception>
    Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the key. Part of "delete all my data" (SC-008): without the key the encrypted bytes
    /// are unrecoverable even if the file survives on disk.
    /// </summary>
    Task DeleteKeyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown when the platform's secure storage cannot be reached, which means the database cannot be
/// opened. Surfaced to the writer as an explicit, actionable message rather than a crash, because
/// the usual cause is a packaging or entitlement problem rather than data loss.
/// </summary>
public sealed class KeyStoreUnavailableException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public KeyStoreUnavailableException()
        : base("The device's secure storage is unavailable, so the encrypted manuscript store cannot be opened.")
    {
    }

    /// <summary>Creates the exception with a specific message.</summary>
    public KeyStoreUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception wrapping the platform failure that caused it.</summary>
    public KeyStoreUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
