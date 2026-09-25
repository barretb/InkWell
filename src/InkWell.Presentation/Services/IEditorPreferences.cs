namespace InkWell.Presentation.Services;

/// <summary>
/// The writer's choice of editing surface, remembered across launches.
/// </summary>
/// <remarks>
/// <para>
/// This is a port rather than a direct call to MAUI's <c>Preferences</c> so the editor page's
/// mode-switching behaviour can be tested without a device, and so the one place that touches a
/// platform API stays a two-line adapter.
/// </para>
/// <para>
/// It holds a display preference and nothing else. It is deliberately not the encrypted store:
/// which editor someone prefers is not manuscript content, and putting it in the database would
/// mean the app could not decide how to render a chapter until it had unlocked the cipher.
/// </para>
/// </remarks>
public interface IEditorPreferences
{
    /// <summary>
    /// Whether to open chapters in the native accessibility-mode editor — plain markdown source in
    /// a platform text control — instead of the live-preview surface (research.md §1).
    /// </summary>
    bool UseAccessibleEditor { get; set; }
}

/// <summary>
/// An in-memory preference store, for tests and for a platform where nothing persisted.
/// </summary>
public sealed class InMemoryEditorPreferences : IEditorPreferences
{
    /// <inheritdoc />
    public bool UseAccessibleEditor { get; set; }
}
