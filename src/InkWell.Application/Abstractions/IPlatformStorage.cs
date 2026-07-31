namespace InkWell.Application.Abstractions;

/// <summary>
/// The device's secure, OS-backed key/value store — Keychain on Apple platforms, Keystore on
/// Android, DPAPI on Windows.
/// </summary>
/// <remarks>
/// This is deliberately a thin port rather than a direct dependency on MAUI's
/// <c>SecureStorage</c>. It keeps the key-bootstrap logic (generate on first run, handle a missing
/// key, erase on "delete all data") in the infrastructure layer where it is unit-testable, while
/// the untestable platform call stays a three-line adapter in the MAUI project.
/// </remarks>
public interface ISecureStore
{
    /// <summary>Reads a value, or null when the key is absent.</summary>
    /// <exception cref="KeyStoreUnavailableException">Secure storage could not be reached.</exception>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Writes a value.</summary>
    /// <exception cref="KeyStoreUnavailableException">Secure storage could not be reached.</exception>
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Removes a value. Removing an absent key is not an error.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Where this device keeps InkWell's data. Abstracted so integration tests can point the whole
/// stack at a temporary directory and delete it afterwards.
/// </summary>
public interface IAppStoragePaths
{
    /// <summary>The full path of the single encrypted SQLite database file.</summary>
    string DatabaseFilePath { get; }
}
