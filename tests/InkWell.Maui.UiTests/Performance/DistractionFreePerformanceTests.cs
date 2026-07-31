using System.Diagnostics;
using InkWell.Maui.UiTests.Harness;

namespace InkWell.Maui.UiTests.Performance;

/// <summary>
/// US2 · SC-006 — entering and leaving focus mode completes in under a second.
/// </summary>
/// <remarks>
/// This measures the host-side transition: flushing the pending edit to the encrypted database and
/// issuing the bridge calls. That is the part with real work in it — a synchronous commit against
/// SQLCipher — and the part this codebase controls. The WebView's own repaint is not included and
/// still needs an on-device measurement (quickstart.md §Performance).
/// </remarks>
public class DistractionFreePerformanceTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Entering_focus_mode_with_a_pending_edit_stays_within_the_budget()
    {
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();
        app.EditorHost.Type(chapterId, "A paragraph the writer has not paused long enough to save.");

        var stopwatch = Stopwatch.StartNew();
        await app.Editor.ToggleDistractionFreeAsync();
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < Budget,
            $"entering focus mode took {stopwatch.ElapsedMilliseconds} ms, budget is {Budget.TotalMilliseconds} ms");
    }

    [Fact]
    public async Task Leaving_focus_mode_stays_within_the_budget()
    {
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();
        await app.Editor.ToggleDistractionFreeAsync();
        app.EditorHost.Type(chapterId, "More prose written in focus mode.");

        var stopwatch = Stopwatch.StartNew();
        await app.Editor.ToggleDistractionFreeAsync();
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < Budget,
            $"leaving focus mode took {stopwatch.ElapsedMilliseconds} ms, budget is {Budget.TotalMilliseconds} ms");
    }

    [Fact]
    public async Task The_transition_stays_within_budget_in_a_large_chapter()
    {
        // SC-004's scale applied to SC-006: a chapter at the top of the realistic range must not
        // make the toggle sluggish. ~5,000 words is a long chapter.
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();
        app.EditorHost.Type(chapterId, string.Join(' ', Enumerable.Repeat("word", 5_000)));

        var stopwatch = Stopwatch.StartNew();
        await app.Editor.ToggleDistractionFreeAsync();
        stopwatch.Stop();

        Assert.Equal(5_000, app.Editor.ChapterWordCount);
        Assert.True(
            stopwatch.Elapsed < Budget,
            $"toggling in a 5,000-word chapter took {stopwatch.ElapsedMilliseconds} ms, budget is {Budget.TotalMilliseconds} ms");
    }

    [Fact]
    public async Task Repeated_toggling_does_not_degrade()
    {
        // Cheap guard against a listener or timer leaking on every transition.
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();
        app.EditorHost.Type(chapterId, "Prose.");

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            await app.Editor.ToggleDistractionFreeAsync();
        }

        stopwatch.Stop();

        Assert.False(app.Editor.IsDistractionFree);
        Assert.True(
            stopwatch.Elapsed < Budget,
            $"twenty transitions took {stopwatch.ElapsedMilliseconds} ms, budget is {Budget.TotalMilliseconds} ms");
    }
}
