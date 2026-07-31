namespace InkWell.Presentation.Services;

/// <summary>
/// Asks the writer to confirm an action that cannot be undone (FR-005).
/// </summary>
/// <remarks>
/// Every destructive path in the app — deleting a manuscript, a chapter, a character, a plot
/// thread, or all app data — goes through this one interface, so "did we remember to confirm?" is a
/// question with a single answer rather than one per call site. It is an interface so that story
/// tests can drive a delete without a dialog.
/// </remarks>
public interface IConfirmationService
{
    /// <summary>
    /// Asks the writer to confirm a destructive action.
    /// </summary>
    /// <param name="title">A short title, for example "Delete chapter?".</param>
    /// <param name="message">What will be lost, named specifically.</param>
    /// <param name="confirmText">The confirming button's label, for example "Delete".</param>
    /// <returns>True only if the writer explicitly confirmed.</returns>
    Task<bool> ConfirmDestructiveAsync(string title, string message, string confirmText);
}

/// <summary>
/// Tells the writer something went wrong, in language that says what to do about it.
/// </summary>
public interface IErrorPresenter
{
    /// <summary>Shows a recoverable problem.</summary>
    Task ShowAsync(string title, string message);
}

/// <summary>
/// Moves between screens. Abstracted so ViewModels — and therefore story tests — never touch
/// <see cref="Shell"/>.
/// </summary>
public interface INavigationService
{
    /// <summary>Navigates to a registered route, optionally passing state.</summary>
    Task GoToAsync(string route, IDictionary<string, object>? parameters = null);

    /// <summary>Returns to the previous screen.</summary>
    Task GoBackAsync();
}
