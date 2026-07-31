using InkWell.Application.Abstractions.Dtos;

namespace InkWell.Presentation.Controls;

/// <summary>Raised when the editor's document changed.</summary>
/// <param name="ChapterId">The chapter being edited.</param>
/// <param name="Markdown">The editor's full markdown buffer.</param>
public sealed record EditorContentChanged(Guid ChapterId, string Markdown);

/// <summary>Raised when the writer drops, pastes, or picks an image.</summary>
/// <param name="ChapterId">The chapter receiving the image.</param>
/// <param name="Bytes">The image bytes.</param>
/// <param name="MimeType">The image's MIME type.</param>
/// <param name="AltText">Alternative text, if any was supplied.</param>
public sealed record EditorImageRequested(Guid ChapterId, byte[] Bytes, string MimeType, string? AltText);

/// <summary>
/// The chapter editor, as the rest of the app sees it (contracts/chapter-editor-bridge.md).
/// </summary>
/// <remarks>
/// The real implementation is CodeMirror 6 inside a <c>HybridWebView</c>. This interface exists so
/// that <c>EditorViewModel</c> — which owns autosave, word counts, and the distraction-free
/// transition — can be tested without a browser engine, and so the native accessibility-mode
/// fallback can be substituted for the same view model.
/// </remarks>
public interface IEditorHost
{
    /// <summary>Raised on every document change; the host debounces these into autosaves.</summary>
    event EventHandler<EditorContentChanged>? ContentChanged;

    /// <summary>Raised when the editor asks for an immediate commit (for example on blur).</summary>
    event EventHandler? FlushRequested;

    /// <summary>Raised when the writer inserts an image.</summary>
    event EventHandler<EditorImageRequested>? ImageRequested;

    /// <summary>Raised when an inserted image has no alternative text (FR-019 accessibility gap).</summary>
    event EventHandler<Guid>? ImageMissingAltText;

    /// <summary>
    /// Raised when the writer asks for focus mode from inside the editor by keyboard.
    /// </summary>
    /// <remarks>
    /// The keyboard route belongs to the editor surface because a focused WebView consumes key
    /// events before any native accelerator sees them. Routing it back through this event means the
    /// keyboard and the visible control converge on the same code inside the view model, so FR-008's
    /// "both routes behave identically" is a property of the design rather than of two
    /// implementations kept in step by hand.
    /// </remarks>
    event EventHandler? DistractionFreeToggleRequested;

    /// <summary>
    /// Raised when the editor surface could not be reached, carrying an explanation.
    /// </summary>
    /// <remarks>
    /// The bridge is asynchronous and its far side is a browser engine, so a message can be dropped
    /// — the page not loaded yet, the bundle missing, the engine wedged. Autosave depends entirely
    /// on the editor reporting changes, so a dropped bridge means nothing is being saved. That must
    /// reach the writer: FR-004 exists to stop work disappearing, and work disappearing quietly is
    /// the worst version of it.
    /// </remarks>
    event EventHandler<string>? BridgeFailed;

    /// <summary>
    /// Opens one chapter in a fresh editor state. Exactly one chapter is ever loaded, never the
    /// whole manuscript, which is what keeps a 150,000-word book responsive (SC-004).
    /// </summary>
    Task LoadChapterAsync(ChapterContent content);

    /// <summary>Hides or restores the surrounding chrome, preserving cursor and content (FR-008).</summary>
    Task SetDistractionFreeAsync(bool enabled);

    /// <summary>Returns focus and the caret to the editor.</summary>
    Task FocusAsync();

    /// <summary>Tells the editor an image is now available under the given reference.</summary>
    Task InsertImageAsync(InlineImageReference image);
}
