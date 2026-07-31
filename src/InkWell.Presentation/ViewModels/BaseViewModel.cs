using CommunityToolkit.Mvvm.ComponentModel;

namespace InkWell.Presentation.ViewModels;

/// <summary>
/// Shared state for every screen: a busy flag and a status line.
/// </summary>
/// <remarks>
/// <see cref="StatusMessage"/> exists because FR-019 forbids conveying state by colour alone. Every
/// screen that shows a state — saved, saving, goal met — puts it here as text that the view binds
/// and the screen reader announces, rather than tinting a control.
/// </remarks>
public abstract partial class BaseViewModel : ObservableObject
{
    /// <summary>Whether a long-running operation is in flight.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The screen's title.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>
    /// A human-readable status, always rendered as text so no state depends on colour (FR-019).
    /// </summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;
}
