using InkWell.Application.Abstractions;

namespace InkWell.Presentation.Services;

/// <summary>
/// MAUI's <see cref="SecureStorage"/> behind the application's <see cref="ISecureStore"/> port.
/// </summary>
/// <remarks>
/// Deliberately thin. All the logic that decides what the key is, when it is created, and what
/// happens when it is missing lives in <c>InkWell.Infrastructure.Security.KeyStore</c>, where it
/// can be tested; this class only makes the platform call. A failure here is translated into
/// <see cref="KeyStoreUnavailableException"/> because the common causes — a missing Keychain
/// entitlement on iOS or Mac Catalyst, a locked Keystore on Android — need a clear message rather
/// than a platform exception (research.md §2).
/// </remarks>
public sealed class MauiSecureStore : ISecureStore
{
    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new KeyStoreUnavailableException(
                "InkWell could not reach this device's secure storage to read the manuscript encryption key.",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        try
        {
            await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new KeyStoreUnavailableException(
                "InkWell could not reach this device's secure storage to store the manuscript encryption key.",
                ex);
        }
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        SecureStorage.Default.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Puts the encrypted database in the app's private data directory, which every platform excludes
/// from other apps and, on Apple platforms, from iCloud document backup.
/// </summary>
public sealed class MauiAppStoragePaths : IAppStoragePaths
{
    /// <inheritdoc />
    public string DatabaseFilePath { get; } = Path.Combine(FileSystem.AppDataDirectory, "inkwell.db3");
}
