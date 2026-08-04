namespace InkWell.Presentation.Services;

/// <summary>Shell-based navigation.</summary>
public sealed class ShellNavigationService : INavigationService
{
    /// <inheritdoc />
    public Task GoToAsync(string route, IDictionary<string, object>? parameters = null)
        => parameters is null
            ? Shell.Current.GoToAsync(route)
            : Shell.Current.GoToAsync(route, parameters);

    /// <inheritdoc />
    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}

/// <summary>
/// Confirmation via the platform's native alert, so the dialog is focus-trapped and announced by
/// the screen reader without any extra work (FR-019).
/// </summary>
public sealed class AlertConfirmationService : IConfirmationService
{
    /// <inheritdoc />
    public Task<bool> ConfirmDestructiveAsync(string title, string message, string confirmText)
        => UiThread.RunAsync(async () =>
        {
            Page? page = AlertHost.CurrentPage();
            if (page is null)
            {
                // No window to ask in means no informed consent, so the safe answer is "no".
                return false;
            }

            return await page.DisplayAlertAsync(title, message, confirmText, "Cancel").ConfigureAwait(true);
        });
}

/// <summary>Error reporting via the platform's native alert.</summary>
public sealed class AlertErrorPresenter : IErrorPresenter
{
    /// <inheritdoc />
    public Task ShowAsync(string title, string message)
        => UiThread.RunAsync(async () =>
        {
            Page? page = AlertHost.CurrentPage();
            if (page is null)
            {
                return;
            }

            await page.DisplayAlertAsync(title, message, "OK").ConfigureAwait(true);
        });
}

/// <summary>
/// Finds the page an alert should be shown over.
/// </summary>
/// <remarks>
/// Both alert services are reached from background work — a failed autosave, a debounced write that
/// threw — so the lookup and the dialog alike are marshalled through <see cref="UiThread"/>. Reading
/// <c>Shell.Current</c> and constructing the native dialog are both UI-thread-only operations.
/// </remarks>
internal static class AlertHost
{
    internal static Page? CurrentPage()
    {
        if (Shell.Current?.CurrentPage is Page current)
        {
            return current;
        }

        IReadOnlyList<Window>? windows = Microsoft.Maui.Controls.Application.Current?.Windows;
        return windows is { Count: > 0 } ? windows[0].Page : null;
    }
}
