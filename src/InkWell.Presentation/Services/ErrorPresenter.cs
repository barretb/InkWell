using InkWell.Application.Abstractions;

namespace InkWell.Presentation.Services;

/// <summary>
/// Turns the ways the encrypted store can fail into something a writer can act on.
/// </summary>
/// <remarks>
/// <para>
/// The failures here are rare and alarming: the device's secure storage is unreachable, or the
/// database will not open. A writer meeting one of them wants to know a single thing — is my novel
/// gone? — and the default behaviour of an unhandled exception answers that in the worst possible
/// way, by closing the app.
/// </para>
/// <para>
/// So each message says what happened, whether the work is still there, and what to do next. The
/// distinction matters: a missing Keychain entitlement is a packaging problem where the data is
/// intact and untouched, while a database that will not open on a device that has been restored
/// from backup is a key that did not travel with it (research.md §2). Those need different
/// sentences, and neither is served by "an error occurred".
/// </para>
/// </remarks>
public static class StoreFailure
{
    /// <summary>
    /// Describes a failure in terms of what the writer should do.
    /// </summary>
    /// <param name="error">The exception the store raised.</param>
    /// <returns>A dialog title and body.</returns>
    public static (string Title, string Message) Describe(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error switch
        {
            KeyStoreUnavailableException => (
                "InkWell cannot unlock your writing",
                "InkWell could not reach this device's secure storage, which holds the key that " +
                "encrypts your manuscripts. Your writing has not been changed or deleted — it is " +
                "still on this device, encrypted. This is usually a problem with how the app was " +
                "installed rather than with your work. Try restarting the app; if it keeps " +
                "happening, reinstalling InkWell from the same source normally restores access."),

            UnauthorizedAccessException => (
                "InkWell does not have permission",
                "InkWell was not allowed to read or write the file it needs. Your writing has not " +
                "been changed. Check that the app still has access to its own storage, and that the " +
                "folder you chose for an export is one you can write to."),

            IOException => (
                "InkWell could not read your writing",
                "The file holding your manuscripts could not be read. Your writing has not been " +
                "deleted. This is often a full disk or a file another program is holding open — " +
                "free some space, close other apps, and try again."),

            InvalidOperationException => (
                "Something InkWell expected was missing",
                error.Message),

            _ => (
                "Something went wrong",
                error.Message),
        };
    }

    /// <summary>
    /// Whether this is a failure the app knows how to explain rather than one that should surface
    /// as a crash.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. Swallowing every exception would turn a genuine bug into a screen that
    /// quietly does nothing, which is harder to diagnose and no better for the writer.
    /// </remarks>
    public static bool IsExpected(Exception error) => error
        is KeyStoreUnavailableException
        or UnauthorizedAccessException
        or IOException
        or InvalidOperationException;

    /// <summary>
    /// Runs a store operation, showing an explanation instead of crashing if it fails.
    /// </summary>
    /// <param name="operation">The work to attempt.</param>
    /// <param name="errors">Where to report a failure.</param>
    /// <returns>True when the operation completed.</returns>
    public static async Task<bool> GuardAsync(Func<Task> operation, IErrorPresenter errors)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(errors);

        try
        {
            await operation().ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            (string title, string message) = Describe(ex);
            await errors.ShowAsync(title, message).ConfigureAwait(true);
            return false;
        }
    }
}
