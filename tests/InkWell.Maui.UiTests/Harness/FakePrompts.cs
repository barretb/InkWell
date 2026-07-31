using InkWell.Presentation.Services;

namespace InkWell.Maui.UiTests.Harness;

/// <summary>
/// A confirmation dialog whose answer the test chooses, so both halves of FR-005 — declining leaves
/// the data alone, confirming removes it — are exercised without a window.
/// </summary>
public sealed class FakeConfirmationService : IConfirmationService
{
    /// <summary>What the writer will answer to the next prompt.</summary>
    public bool NextAnswer { get; set; }

    /// <summary>Every prompt shown, as title and message.</summary>
    public List<(string Title, string Message)> Prompts { get; } = [];

    /// <inheritdoc />
    public Task<bool> ConfirmDestructiveAsync(string title, string message, string confirmText)
    {
        Prompts.Add((title, message));
        return Task.FromResult(NextAnswer);
    }
}

/// <summary>Records what the writer was told went wrong.</summary>
public sealed class FakeErrorPresenter : IErrorPresenter
{
    /// <summary>Every error shown.</summary>
    public List<(string Title, string Message)> Errors { get; } = [];

    /// <inheritdoc />
    public Task ShowAsync(string title, string message)
    {
        Errors.Add((title, message));
        return Task.CompletedTask;
    }
}

/// <summary>Records navigation without a Shell.</summary>
public sealed class FakeNavigationService : INavigationService
{
    /// <summary>Every route navigated to, with its parameters.</summary>
    public List<(string Route, IDictionary<string, object>? Parameters)> Navigations { get; } = [];

    /// <summary>How many times the writer went back.</summary>
    public int BackCount { get; private set; }

    /// <inheritdoc />
    public Task GoToAsync(string route, IDictionary<string, object>? parameters = null)
    {
        Navigations.Add((route, parameters));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task GoBackAsync()
    {
        BackCount++;
        return Task.CompletedTask;
    }
}
