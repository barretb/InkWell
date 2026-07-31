using CommunityToolkit.Mvvm.ComponentModel;
using InkWell.Domain.Entities;

namespace InkWell.Presentation.ViewModels;

/// <summary>
/// One character as the list edits it.
/// </summary>
/// <remarks>
/// The domain entity is a plain object with no change notification, which is right for the domain
/// and useless for a two-way bound editor: the list would never learn that the writer had typed.
/// This wrapper raises <see cref="Edited"/> on every keystroke so the screen can autosave, and
/// keeps the entity itself free of presentation concerns.
/// </remarks>
public sealed partial class CharacterEditor : ObservableObject
{
    /// <summary>Wraps a stored character.</summary>
    public CharacterEditor(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        Id = character.Id;

        // Assigning the properties raises Edited, which is harmless: nothing has subscribed yet,
        // because callers construct the editor and then attach.
        Name = character.Name;
        Notes = character.Notes;
    }

    /// <summary>The character's identifier.</summary>
    public Guid Id { get; }

    /// <summary>Raised whenever the writer changes a field.</summary>
    public event EventHandler? Edited;

    /// <summary>The character's name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Freeform continuity notes.</summary>
    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    partial void OnNameChanged(string value) => Edited?.Invoke(this, EventArgs.Empty);

    partial void OnNotesChanged(string value) => Edited?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// One plot thread as the list edits it. See <see cref="CharacterEditor"/> for why this wrapper
/// exists.
/// </summary>
public sealed partial class PlotThreadEditor : ObservableObject
{
    /// <summary>Wraps a stored plot thread.</summary>
    public PlotThreadEditor(PlotThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        Id = thread.Id;
        Title = thread.Title;
        Notes = thread.Notes;
    }

    /// <summary>The thread's identifier.</summary>
    public Guid Id { get; }

    /// <summary>Raised whenever the writer changes a field.</summary>
    public event EventHandler? Edited;

    /// <summary>The thread's title.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Freeform notes about the thread.</summary>
    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    partial void OnTitleChanged(string value) => Edited?.Invoke(this, EventArgs.Empty);

    partial void OnNotesChanged(string value) => Edited?.Invoke(this, EventArgs.Empty);
}
