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

/// <summary>
/// The platform's own "save as" and "choose folder" dialogs, via the MAUI Community Toolkit.
/// </summary>
/// <remarks>
/// Deliberately thin, and deliberately the only thing in the app that can name an export
/// destination. Every path an export writes to comes from a dialog the writer confirmed, which is
/// what makes FR-017's "nothing leaves the device except through an explicit, user-initiated
/// export" true of the code and not only of the intent.
/// </remarks>
public sealed class ToolkitFileDestinationPicker : IFileDestinationPicker
{
    /// <inheritdoc />
    public async Task<string?> PickSaveLocationAsync(string suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        // The toolkit's FileSaver both asks and writes, so it is handed an empty stream: the file
        // it creates is the destination, and the exporter fills it. This keeps the "where" and the
        // "what" in separate layers.
        using var empty = new MemoryStream();
        CommunityToolkit.Maui.Storage.FileSaverResult result =
            await CommunityToolkit.Maui.Storage.FileSaver.Default
                .SaveAsync(suggestedFileName, empty, CancellationToken.None)
                .ConfigureAwait(false);

        return result.IsSuccessful ? result.FilePath : null;
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync()
    {
        CommunityToolkit.Maui.Storage.FolderPickerResult result =
            await CommunityToolkit.Maui.Storage.FolderPicker.Default
                .PickAsync(CancellationToken.None)
                .ConfigureAwait(false);

        return result.IsSuccessful ? result.Folder?.Path : null;
    }
}

/// <summary>
/// The editing-surface preference, in MAUI's <see cref="Preferences"/>.
/// </summary>
/// <remarks>
/// Plain preferences rather than the encrypted database, deliberately: this is a display setting,
/// not manuscript content, and the app has to know which editor to build before it has unlocked the
/// cipher.
/// </remarks>
public sealed class MauiEditorPreferences : IEditorPreferences
{
    private const string UseAccessibleEditorKey = "editor.accessibility_mode";

    /// <inheritdoc />
    public bool UseAccessibleEditor
    {
        get => Preferences.Default.Get(UseAccessibleEditorKey, defaultValue: false);
        set => Preferences.Default.Set(UseAccessibleEditorKey, value);
    }
}
