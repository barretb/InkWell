using InkWell.Application.Abstractions.Dtos;

namespace InkWell.Presentation.Controls;

/// <summary>
/// The document state and text rules of the native accessibility-mode editor, with no MAUI control
/// attached.
/// </summary>
/// <remarks>
/// <para>
/// Split out of <see cref="AccessibleEditorFallbackView"/> because a <c>VisualElement</c> cannot be
/// constructed outside a running MAUI app, which would leave the fallback's actual behaviour — what
/// markdown it holds, what an image insert writes, whether opening a chapter is mistaken for typing
/// — untestable. That behaviour is where the fallback can go wrong; the control around it is a
/// shell that shows a string and reports edits.
/// </para>
/// <para>
/// This is the same division the project already uses for the web editor: the view model and its
/// rules are tested, the device-bound surface is not, and the surface is kept thin enough that
/// there is nothing in it to get wrong.
/// </para>
/// </remarks>
public sealed class AccessibleEditorDocument
{
    /// <summary>
    /// What an empty chapter says for itself, so a fresh chapter is guidance rather than a blank
    /// screen (spec.md edge case "empty states").
    /// </summary>
    /// <remarks>
    /// Worded identically to the web editor's placeholder in
    /// <c>Resources/Raw/wwwroot/index.html</c> (<c>data-empty-hint</c>), so switching surfaces does
    /// not change what a new chapter tells the writer.
    /// </remarks>
    public const string EmptyChapterHint = "Start writing. Your words are saved automatically.";

    private bool _loading;

    /// <summary>The chapter currently open, or <see cref="Guid.Empty"/> before the first load.</summary>
    public Guid ChapterId { get; private set; }

    /// <summary>The markdown the writer is editing.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>The caret offset within <see cref="Text"/>.</summary>
    public int Caret { get; private set; }

    /// <summary>Whether the chrome-free layout is applied.</summary>
    public bool IsDistractionFree { get; private set; }

    /// <summary>
    /// The explanation shown above the text, so nobody has to work out why their formatting looks
    /// like punctuation — or wonder whether their images were lost.
    /// </summary>
    public string ModeNotice { get; private set; } = DefaultNotice;

    private const string DefaultNotice =
        "Accessibility mode. This chapter is shown as markdown source, so formatting appears as the " +
        "symbols that produce it. Your writing is saved exactly as it is in the formatted editor.";

    /// <summary>Opens a chapter, replacing whatever was loaded.</summary>
    public void Load(ChapterContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _loading = true;
        try
        {
            ChapterId = content.Id;
            Text = content.ContentMarkdown;
            Caret = 0;
            ModeNotice = content.Images.Count == 0
                ? DefaultNotice
                : "Accessibility mode. This chapter is shown as markdown source. " +
                  $"{Describe(content.Images.Count)} in this chapter appear as reference text rather than " +
                  "pictures. Your writing is saved exactly as it is in the formatted editor.";
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Accepts new text from the control, reporting whether it is an edit worth telling the view
    /// model about.
    /// </summary>
    /// <returns>
    /// False while a chapter is being loaded or before one is open. Setting a native
    /// <c>Editor.Text</c> raises its change event, and treating that as typing would queue an
    /// autosave for a document nobody touched and add a freshly opened chapter's words to the day's
    /// total (FR-012).
    /// </returns>
    public bool TryAcceptEdit(string? newText, out EditorContentChanged change)
    {
        change = default!;
        if (_loading || ChapterId == Guid.Empty)
        {
            return false;
        }

        string text = newText ?? string.Empty;
        if (string.Equals(text, Text, StringComparison.Ordinal))
        {
            return false;
        }

        Text = text;
        Caret = Math.Clamp(Caret, 0, Text.Length);
        change = new EditorContentChanged(ChapterId, Text);
        return true;
    }

    /// <summary>Moves the caret, as the control reports it.</summary>
    public void SetCaret(int offset) => Caret = Math.Clamp(offset, 0, Text.Length);

    /// <summary>
    /// Applies or removes the chrome-free layout.
    /// </summary>
    /// <remarks>
    /// Neither the text nor the caret is touched, which is what makes SC-006's "return to their
    /// exact cursor position" a property of the design rather than something restored by hand —
    /// the same reasoning as the web editor's CSS-class toggle (research.md §5.6).
    /// </remarks>
    public void SetDistractionFree(bool enabled) => IsDistractionFree = enabled;

    /// <summary>
    /// Writes an image reference at the caret and returns the resulting text.
    /// </summary>
    /// <remarks>
    /// Exactly the markdown the web editor writes, so a chapter edited in either surface is one
    /// document rather than two dialects of one.
    /// </remarks>
    public string InsertImage(InlineImageReference image)
    {
        ArgumentNullException.ThrowIfNull(image);

        string snippet = $"![{image.AltText}](inkwell-img://{image.Id})";
        int at = Math.Clamp(Caret, 0, Text.Length);

        Text = Text.Insert(at, snippet);
        Caret = at + snippet.Length;
        return Text;
    }

    private static string Describe(int count) => count == 1 ? "1 image" : $"{count} images";
}
