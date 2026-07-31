using InkWell.Maui.UiTests.Harness;

namespace InkWell.Maui.UiTests;

/// <summary>
/// Regression cover for the bug where chapter content did not save.
/// </summary>
/// <remarks>
/// <para>
/// The cause was a handshake that was designed but never wired up. The web editor announced itself
/// with <c>editorReady</c>; the host ignored that message and pushed <c>loadChapter</c> as soon as
/// the page appeared — before the WebView had finished loading the bundle. The message was
/// discarded, so the editor never learned which chapter it was editing, and its
/// <c>reportContent</c> guard (<c>if (!view || !chapterId) return;</c>) then suppressed every
/// single change. Nothing was ever saved, and nothing said so.
/// </para>
/// <para>
/// The fix has two halves, and both matter: the host now waits for readiness before sending, and a
/// bridge that never comes up is reported instead of failing silently. These tests cover the second
/// half plus the wiring; the handshake itself needs a device (see research.md §5.2).
/// </para>
/// </remarks>
public class EditorBridgeFailureTests
{
    [Fact]
    public async Task Opening_a_chapter_pushes_it_into_the_editor()
    {
        // The message whose loss caused the bug.
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();

        Assert.NotNull(app.EditorHost.LoadedChapter);
        Assert.Equal(chapterId, app.EditorHost.LoadedChapter!.Id);
    }

    [Fact]
    public async Task An_unreachable_editor_is_reported_rather_than_failing_silently()
    {
        await using var app = new AppHarness();
        await app.OpenNewChapterAsync();

        app.EditorHost.FailBridge();

        // Told twice: in the status line the writer may be watching, and in a dialog they cannot
        // miss. A writer who is told immediately loses a paragraph; one who is not loses an evening.
        Assert.Contains("Not saved", app.Editor.StatusMessage, StringComparison.Ordinal);
        Assert.Single(app.Errors.Errors);
        Assert.Equal("Your typing is not being saved", app.Errors.Errors[0].Title);
    }

    [Fact]
    public async Task The_failure_message_tells_the_writer_what_to_do()
    {
        await using var app = new AppHarness();
        await app.OpenNewChapterAsync();

        app.EditorHost.FailBridge(
            "The writing surface did not finish loading, so your typing is not being saved. " +
            "Close and reopen the chapter. If it keeps happening, copy your text somewhere safe.");

        string message = app.Errors.Errors[0].Message;
        Assert.Contains("not being saved", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("copy your text somewhere safe", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Typing_still_saves_normally_when_the_bridge_is_healthy()
    {
        // The other half of the regression: prove the ordinary path works end to end.
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();

        app.EditorHost.Type(chapterId, "The snow came early that year.");
        await app.Editor.FlushAsync();

        Assert.Equal("The snow came early that year.", await app.ReadStoredMarkdownAsync(chapterId));
        Assert.Equal("Saved", app.Editor.StatusMessage);
    }
}
