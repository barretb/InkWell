using InkWell.Application.Abstractions.Dtos;
using InkWell.Presentation.Controls;

namespace InkWell.Maui.UiTests.Accessibility;

/// <summary>
/// T070 · SC-007 — the rules of the native accessibility-mode editor: markdown in, markdown out,
/// focus mode that never disturbs the document, and the same image markup the web editor writes.
/// </summary>
/// <remarks>
/// These drive <see cref="AccessibleEditorDocument"/> rather than the <c>ContentView</c> around it,
/// because a MAUI <c>VisualElement</c> cannot be constructed outside a running app. That is the same
/// line the project already draws for the web editor: the rules are tested here, and how a platform
/// text control actually narrates is the platform's contract, confirmed in the device pass (T130).
/// </remarks>
public class AccessibleEditorFallbackTests
{
    private static ChapterContent Chapter(string markdown, params InlineImageReference[] images)
        => new(Guid.NewGuid(), Guid.NewGuid(), "Snowfall", markdown, images);

    [Fact]
    public void It_opens_a_chapter_as_markdown_source()
    {
        var document = new AccessibleEditorDocument();

        document.Load(Chapter("# Snowfall\n\nElin watched the **mill** burn."));

        // Shown as written: the markdown is the document, not a rendering of it.
        Assert.Equal("# Snowfall\n\nElin watched the **mill** burn.", document.Text);
        Assert.Equal(0, document.Caret);
    }

    [Fact]
    public void Loading_a_chapter_is_not_mistaken_for_the_writer_typing_it()
    {
        // Setting a native Editor's Text raises TextChanged. If that reached the view model it would
        // queue an autosave for a document nobody touched and add an opened chapter's words to the
        // day's total (FR-012).
        var document = new AccessibleEditorDocument();

        document.Load(Chapter("Already written prose."));

        Assert.False(document.TryAcceptEdit("Already written prose.", out _));
    }

    [Fact]
    public void An_edit_before_any_chapter_is_open_is_ignored()
    {
        var document = new AccessibleEditorDocument();

        Assert.False(document.TryAcceptEdit("stray keystroke", out _));
        Assert.Equal(string.Empty, document.Text);
    }

    [Fact]
    public void A_real_edit_is_reported_against_the_open_chapter()
    {
        var document = new AccessibleEditorDocument();
        ChapterContent chapter = Chapter("Elin watched");
        document.Load(chapter);

        bool reported = document.TryAcceptEdit("Elin watched the mill burn.", out EditorContentChanged change);

        Assert.True(reported);
        Assert.Equal(chapter.Id, change.ChapterId);
        Assert.Equal("Elin watched the mill burn.", change.Markdown);
        Assert.Equal("Elin watched the mill burn.", document.Text);
    }

    [Fact]
    public void Focus_mode_changes_nothing_about_the_document()
    {
        // The same guarantee the web host gives (research.md §5.6): SC-006's "exact cursor position"
        // holds because nothing replaces the document, not because an offset is saved and restored.
        var document = new AccessibleEditorDocument();
        document.Load(Chapter("Elin watched the mill burn."));
        document.SetCaret(5);

        document.SetDistractionFree(true);
        Assert.True(document.IsDistractionFree);
        Assert.Equal("Elin watched the mill burn.", document.Text);
        Assert.Equal(5, document.Caret);

        document.SetDistractionFree(false);
        Assert.False(document.IsDistractionFree);
        Assert.Equal("Elin watched the mill burn.", document.Text);
        Assert.Equal(5, document.Caret);
    }

    [Fact]
    public void An_inserted_image_is_written_as_the_same_markdown_the_web_editor_writes()
    {
        // A chapter edited in either surface has to be one document, not two dialects of one.
        var document = new AccessibleEditorDocument();
        document.Load(Chapter("Before after"));
        document.SetCaret("Before ".Length);

        var image = new InlineImageReference(Guid.NewGuid(), "image/png", "The frozen mill", "data:image/png;base64,AA==");
        string result = document.InsertImage(image);

        Assert.Equal($"Before ![The frozen mill](inkwell-img://{image.Id})after", result);
        Assert.Equal($"Before ![The frozen mill](inkwell-img://{image.Id})".Length, document.Caret);
    }

    [Fact]
    public void The_caret_never_escapes_the_document()
    {
        var document = new AccessibleEditorDocument();
        document.Load(Chapter("short"));

        document.SetCaret(500);
        Assert.Equal(5, document.Caret);

        document.SetCaret(-3);
        Assert.Equal(0, document.Caret);
    }

    [Fact]
    public void A_chapter_with_images_says_so_rather_than_letting_them_seem_lost()
    {
        // The images are in the chapter but cannot be drawn here. Silence would read as data loss.
        var document = new AccessibleEditorDocument();

        document.Load(Chapter(
            "text",
            new InlineImageReference(Guid.NewGuid(), "image/png", "one", "data:image/png;base64,AA==")));
        Assert.Contains("1 image", document.ModeNotice, StringComparison.Ordinal);

        document.Load(Chapter(
            "text",
            new InlineImageReference(Guid.NewGuid(), "image/png", "one", "data:image/png;base64,AA=="),
            new InlineImageReference(Guid.NewGuid(), "image/png", "two", "data:image/png;base64,AA==")));
        Assert.Contains("2 images", document.ModeNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chapter_without_images_gets_the_plain_notice()
    {
        var document = new AccessibleEditorDocument();

        document.Load(Chapter("no pictures here"));

        Assert.DoesNotContain("image", document.ModeNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("markdown source", document.ModeNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_a_second_chapter_replaces_the_first_completely()
    {
        var document = new AccessibleEditorDocument();
        document.Load(Chapter("first chapter"));
        document.SetCaret(5);

        ChapterContent second = Chapter("second chapter");
        document.Load(second);

        Assert.Equal(second.Id, document.ChapterId);
        Assert.Equal("second chapter", document.Text);
        Assert.Equal(0, document.Caret);
    }
}
