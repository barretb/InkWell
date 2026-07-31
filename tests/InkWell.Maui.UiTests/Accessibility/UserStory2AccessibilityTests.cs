using InkWell.Maui.UiTests.Harness;

namespace InkWell.Maui.UiTests.Accessibility;

/// <summary>
/// US2 · FR-019, SC-007 — focus mode is reachable and reversible by keyboard alone, and the mode
/// change is announced in words.
/// </summary>
/// <remarks>
/// These cover the parts of accessibility that are decidable in code: that a keyboard-only route
/// exists and completes the journey, and that state is carried by text rather than by appearance.
/// Contrast ratios and actual screen-reader output still need a device pass (quickstart.md
/// §Cross-cutting step 4).
/// </remarks>
public class UserStory2AccessibilityTests
{
    [Fact]
    public async Task The_whole_focus_mode_journey_is_completable_by_keyboard_alone()
    {
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();

        // Enter by keyboard.
        app.EditorHost.PressFocusModeShortcut();
        await WaitUntilAsync(() => app.Editor.IsDistractionFree);
        Assert.True(app.Editor.IsDistractionFree);

        // Write by keyboard.
        app.EditorHost.Type(chapterId, "Typed without touching a pointer.");
        await app.Editor.FlushAsync();

        // Leave by keyboard.
        app.EditorHost.PressFocusModeShortcut();
        await WaitUntilAsync(() => !app.Editor.IsDistractionFree);
        Assert.False(app.Editor.IsDistractionFree);

        Assert.Equal("Typed without touching a pointer.", await app.ReadStoredMarkdownAsync(chapterId));
    }

    [Fact]
    public async Task The_mode_change_is_announced_as_text()
    {
        // FR-019: no state may be conveyed by appearance alone. The status line is what the screen
        // reader speaks, so it has to say which mode is now in force.
        await using var app = new AppHarness();
        await app.OpenNewChapterAsync();

        await app.Editor.ToggleDistractionFreeAsync();
        Assert.Contains("Distraction-free mode on", app.Editor.StatusMessage, StringComparison.Ordinal);

        await app.Editor.ToggleDistractionFreeAsync();
        Assert.Contains("Distraction-free mode off", app.Editor.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_announcement_names_the_way_out()
    {
        // A chrome-less screen that does not say how to leave is a trap for anyone who did not read
        // the documentation, and worse for a screen-reader user who cannot see the exit control.
        await using var app = new AppHarness();
        await app.OpenNewChapterAsync();

        await app.Editor.ToggleDistractionFreeAsync();

        Assert.Contains("Escape", app.Editor.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_state_stays_readable_as_text_in_focus_mode()
    {
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();

        await app.Editor.ToggleDistractionFreeAsync();
        app.EditorHost.Type(chapterId, "Some words.");
        await app.Editor.FlushAsync();

        Assert.Equal("Saved", app.Editor.StatusMessage);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }
}
