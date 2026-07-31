using InkWell.Maui.UiTests.Harness;

namespace InkWell.Maui.UiTests;

/// <summary>
/// User Story 2 end to end: "Open a chapter, activate distraction-free mode, confirm that
/// non-essential UI is hidden and the text remains fully editable, then exit and confirm the full
/// interface returns with content intact."
/// </summary>
public class UserStory2Tests
{
    [Fact]
    public async Task The_writer_enters_focus_mode_keeps_writing_and_leaves_with_everything_intact()
    {
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();

        // Some prose exists before the writer decides to focus.
        app.EditorHost.Type(chapterId, "The snow came early that year. ");
        await app.Editor.FlushAsync();

        // 1. Activate distraction-free mode.
        await app.Editor.ToggleDistractionFreeAsync();
        Assert.True(app.Editor.IsDistractionFree);
        Assert.True(app.EditorHost.IsDistractionFree);

        // 2. Typing behaves identically and still autosaves without an explicit save (US2 scenario 2).
        app.EditorHost.Type(chapterId, "It fell for nine days without pause.");
        await app.Editor.FlushAsync();
        Assert.Equal(
            "The snow came early that year. It fell for nine days without pause.",
            await app.ReadStoredMarkdownAsync(chapterId));

        // 3. Exit; the full interface returns with cursor and content unchanged (US2 scenario 3).
        int caret = app.EditorHost.Caret;
        await app.Editor.ToggleDistractionFreeAsync();

        Assert.False(app.Editor.IsDistractionFree);
        Assert.False(app.EditorHost.IsDistractionFree);
        Assert.Equal(caret, app.EditorHost.Caret);
        Assert.Equal(
            "The snow came early that year. It fell for nine days without pause.",
            app.EditorHost.Document);
    }

    [Fact]
    public async Task Autosave_behaves_identically_in_both_modes()
    {
        // US2 scenario 2. Same debounce, same commit, same result — the mode is presentation only.
        await using var app = new AppHarness(autoSaveDebounce: TimeSpan.FromMilliseconds(40));
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();

        app.EditorHost.Type(chapterId, "Written with the chrome showing.");
        await WaitForStoredAsync(app, chapterId, "Written with the chrome showing.");

        await app.Editor.ToggleDistractionFreeAsync();

        app.EditorHost.Type(chapterId, " Written with it hidden.");
        await WaitForStoredAsync(
            app, chapterId, "Written with the chrome showing. Written with it hidden.");
    }

    [Fact]
    public async Task Entering_focus_mode_commits_whatever_was_pending_first()
    {
        // The transition must never be the thing that loses a sentence (FR-004, SC-003).
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();

        app.EditorHost.Type(chapterId, "Typed but not yet idle.");
        Assert.Equal(string.Empty, await app.ReadStoredMarkdownAsync(chapterId));

        await app.Editor.ToggleDistractionFreeAsync();

        Assert.Equal("Typed but not yet idle.", await app.ReadStoredMarkdownAsync(chapterId));
    }

    [Fact]
    public async Task Work_written_in_focus_mode_survives_a_restart()
    {
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();

        await app.Editor.ToggleDistractionFreeAsync();
        app.EditorHost.Type(chapterId, "A whole scene written in one sitting.");
        await app.Editor.FlushAsync();

        await app.RestartAsync();

        Assert.Equal(
            "A whole scene written in one sitting.",
            await app.ReadStoredMarkdownAsync(chapterId));
    }

    [Fact]
    public async Task Word_counts_keep_updating_while_the_chrome_is_hidden()
    {
        // The counts still exist in focus mode — they are part of the minimal essential controls,
        // not chrome (FR-007, FR-009).
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();

        await app.Editor.ToggleDistractionFreeAsync();
        app.EditorHost.Type(chapterId, "The snow came early that year.");
        await app.Editor.FlushAsync();

        Assert.Equal(6, app.Editor.ChapterWordCount);
        Assert.Equal(6, app.Editor.ManuscriptWordCount);
        Assert.Contains("6 words in this chapter", app.Editor.CountsSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Focus_mode_is_not_sticky_across_opening_another_chapter()
    {
        // A fresh editor starts in the normal layout, so the writer is never dropped into a
        // chrome-less screen they did not ask for.
        await using var app = new AppHarness();
        await app.OpenNewChapterAsync();
        await app.Editor.ToggleDistractionFreeAsync();
        Assert.True(app.Editor.IsDistractionFree);

        await app.RestartAsync();
        await app.OpenNewChapterAsync("Second book", "First chapter");

        Assert.False(app.Editor.IsDistractionFree);
    }

    private static async Task WaitForStoredAsync(AppHarness app, Guid chapterId, string expected)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await app.ReadStoredMarkdownAsync(chapterId) == expected)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Equal(expected, await app.ReadStoredMarkdownAsync(chapterId));
    }
}
