using InkWell.Application.Abstractions.Dtos;
using InkWell.Presentation.Controls;

namespace InkWell.Maui.UiTests.Harness;

/// <summary>
/// Stands in for the CodeMirror surface so story tests can drive the real
/// <see cref="Presentation.ViewModels.EditorViewModel"/> without a WebView.
/// </summary>
/// <remarks>
/// It models the two things the distraction-free story actually asserts: the editor holds a
/// document and a caret, and neither is touched by a mode change. If the host is ever asked to
/// replace the document during a toggle, <see cref="DocumentReplacements"/> records it and the test
/// fails.
/// </remarks>
public sealed class FakeEditorHost : IEditorHost
{
    /// <summary>Every bridge call the view model made, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The chapter currently open, if any.</summary>
    public ChapterContent? LoadedChapter { get; private set; }

    /// <summary>The editor's document — plain markdown, exactly as CodeMirror would hold it.</summary>
    public string Document { get; private set; } = string.Empty;

    /// <summary>The caret offset within <see cref="Document"/>.</summary>
    public int Caret { get; private set; }

    /// <summary>Whether the chrome-free layout is applied.</summary>
    public bool IsDistractionFree { get; private set; }

    /// <summary>How many times focus and caret were restored.</summary>
    public int FocusRestoreCount { get; private set; }

    /// <summary>How many times the document was replaced wholesale.</summary>
    public int DocumentReplacements { get; private set; }

    /// <summary>Images the host was told to render inline.</summary>
    public List<InlineImageReference> InsertedImages { get; } = [];

    /// <inheritdoc />
    public event EventHandler<EditorContentChanged>? ContentChanged;

    /// <inheritdoc />
    public event EventHandler? FlushRequested;

    /// <inheritdoc />
    public event EventHandler<EditorImageRequested>? ImageRequested;

    /// <inheritdoc />
    public event EventHandler<Guid>? ImageMissingAltText;

    /// <inheritdoc />
    public event EventHandler? DistractionFreeToggleRequested;

    /// <inheritdoc />
    public event EventHandler<string>? BridgeFailed;

    /// <summary>Simulates the web editor never loading, so the bridge times out.</summary>
    public void FailBridge(string reason = "The writing surface did not finish loading.")
        => BridgeFailed?.Invoke(this, reason);

    /// <inheritdoc />
    public Task LoadChapterAsync(ChapterContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        LoadedChapter = content;
        Document = content.ContentMarkdown;
        Caret = 0;
        DocumentReplacements++;
        Calls.Add("loadChapter");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetDistractionFreeAsync(bool enabled)
    {
        // Only layout changes; the document and caret are untouched, exactly as the CSS-class
        // implementation in styles.css behaves.
        IsDistractionFree = enabled;
        Calls.Add($"setDistractionFree:{enabled}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FocusAsync()
    {
        FocusRestoreCount++;
        Calls.Add("focusEditor");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InsertImageAsync(InlineImageReference image)
    {
        ArgumentNullException.ThrowIfNull(image);
        InsertedImages.Add(image);
        string snippet = $"![{image.AltText}](inkwell-img://{image.Id})";
        Document = Document.Insert(Caret, snippet);
        Caret += snippet.Length;
        Calls.Add("insertImage");
        return Task.CompletedTask;
    }

    /// <summary>Simulates the writer typing, which is what raises <c>contentChanged</c>.</summary>
    public void Type(Guid chapterId, string text)
    {
        Document = Document.Insert(Caret, text);
        Caret += text.Length;
        ContentChanged?.Invoke(this, new EditorContentChanged(chapterId, Document));
    }

    /// <summary>
    /// Simulates select-all-and-retype: the document becomes exactly <paramref name="markdown"/>.
    /// </summary>
    /// <remarks>
    /// Lets a test state the resulting prose outright — "the chapter now holds 500 words" — rather
    /// than accumulating keystrokes and reasoning about the total. It goes through the same
    /// <c>contentChanged</c> path as typing, so autosave sees no difference.
    /// </remarks>
    public void Replace(Guid chapterId, string markdown)
    {
        Document = markdown;
        Caret = markdown.Length;
        ContentChanged?.Invoke(this, new EditorContentChanged(chapterId, Document));
    }

    /// <summary>Moves the caret, as clicking or arrowing through the text would.</summary>
    public void PlaceCaret(int offset) => Caret = offset;

    /// <summary>Simulates the editor losing focus, which asks the host to commit immediately.</summary>
    public void Blur() => FlushRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Simulates the in-editor keyboard shortcut for focus mode (FR-008).</summary>
    public void PressFocusModeShortcut() => DistractionFreeToggleRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Simulates the writer pasting or dropping an image.</summary>
    public void PasteImage(Guid chapterId, byte[] bytes, string mimeType, string? altText)
        => ImageRequested?.Invoke(this, new EditorImageRequested(chapterId, bytes, mimeType, altText));

    /// <summary>Simulates the editor reporting an image with no alternative text.</summary>
    public void ReportMissingAltText(Guid imageId) => ImageMissingAltText?.Invoke(this, imageId);
}
