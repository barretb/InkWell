namespace InkWell.Presentation.Services;

/// <summary>
/// Where an export should be written, as chosen by the writer through the platform's own save
/// dialog.
/// </summary>
/// <remarks>
/// <para>
/// A port rather than a direct call into <c>CommunityToolkit.Maui.Storage</c>, for the usual reason
/// and for one specific to this feature: export is the only path by which manuscript content leaves
/// the device (FR-017), so "the destination came from the writer and from nowhere else" is a
/// property worth being able to test. With the picker behind an interface, a test can assert that
/// the view model writes exactly where it was told and never invents a path of its own.
/// </para>
/// <para>
/// Every method returns null when the writer cancels, which is a normal outcome and not an error.
/// </para>
/// </remarks>
public interface IFileDestinationPicker
{
    /// <summary>
    /// Asks the writer where to save one file.
    /// </summary>
    /// <param name="suggestedFileName">The name to offer, which the writer may change.</param>
    /// <returns>The chosen full path, or null if they cancelled.</returns>
    Task<string?> PickSaveLocationAsync(string suggestedFileName);

    /// <summary>
    /// Asks the writer for a folder, for exports that produce one file per chapter.
    /// </summary>
    /// <returns>The chosen folder, or null if they cancelled.</returns>
    Task<string?> PickFolderAsync();
}
