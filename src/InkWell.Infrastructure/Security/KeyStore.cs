using System.Security.Cryptography;
using InkWell.Application.Abstractions;

namespace InkWell.Infrastructure.Security;

/// <summary>
/// Mints and holds the 256-bit key that encrypts the manuscript database (FR-016).
/// </summary>
/// <remarks>
/// <para>
/// The key is generated once, on first run, from a cryptographic RNG and then lives only in the
/// platform's secure storage — Keychain, Android Keystore, or DPAPI. It is never derived from
/// anything guessable, never written next to the database, and never leaves the device.
/// </para>
/// <para>
/// The in-flight lookup is cached as a task rather than re-run per caller, because the database is
/// opened on the autosave path and a Keychain round trip there would show up as typing latency. A
/// failed lookup is not cached, so a transient secure-storage failure can be retried.
/// </para>
/// <para>
/// Losing the key means losing the data: there is no recovery path and no escrow, by design
/// (constitution §VIII). That is why "delete all data" deletes the key, and why a missing key is
/// surfaced explicitly instead of silently creating a second, empty database (research.md §2).
/// </para>
/// </remarks>
public sealed class KeyStore : IKeyStore
{
    /// <summary>The secure-storage key under which the database cipher key is filed.</summary>
    public const string SecureStoreKey = "inkwell.database.key.v1";

    private const int KeyLengthBytes = 32;

    private readonly ISecureStore _secureStore;
    private readonly object _sync = new();
    private Task<string>? _keyTask;

    /// <summary>Creates a key store over the device's secure storage.</summary>
    /// <param name="secureStore">The platform secure-storage adapter.</param>
    public KeyStore(ISecureStore secureStore)
    {
        ArgumentNullException.ThrowIfNull(secureStore);
        _secureStore = secureStore;
    }

    /// <inheritdoc />
    public async Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken = default)
    {
        Task<string> task;
        lock (_sync)
        {
            task = _keyTask ??= LoadOrCreateAsync(cancellationToken);
        }

        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_keyTask, task))
                {
                    _keyTask = null;
                }
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteKeyAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _keyTask = null;
        }

        await _secureStore.RemoveAsync(SecureStoreKey, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        string? existing;
        try
        {
            existing = await _secureStore.GetAsync(SecureStoreKey, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyStoreUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KeyStoreUnavailableException(
                "InkWell could not read the manuscript encryption key from this device's secure storage.",
                ex);
        }

        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        string created = Convert.ToHexString(RandomNumberGenerator.GetBytes(KeyLengthBytes));

        try
        {
            await _secureStore.SetAsync(SecureStoreKey, created, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyStoreUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KeyStoreUnavailableException(
                "InkWell could not store the manuscript encryption key in this device's secure storage.",
                ex);
        }

        return created;
    }
}
