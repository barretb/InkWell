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
    public async Task<bool> ConfirmDestructiveAsync(string title, string message, string confirmText)
    {
        Page? page = Shell.Current?.CurrentPage ?? Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
        {
            // No window to ask in means no informed consent, so the safe answer is "no".
            return false;
        }

        return await page.DisplayAlert(title, message, confirmText, "Cancel").ConfigureAwait(false);
    }
}

/// <summary>Error reporting via the platform's native alert.</summary>
public sealed class AlertErrorPresenter : IErrorPresenter
{
    /// <inheritdoc />
    public async Task ShowAsync(string title, string message)
    {
        Page? page = Shell.Current?.CurrentPage ?? Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
        {
            return;
        }

        await page.DisplayAlert(title, message, "OK").ConfigureAwait(false);
    }
}
