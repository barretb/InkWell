using InkWell.Maui.UiTests.Harness;

namespace InkWell.Maui.UiTests;

/// <summary>
/// US2 · contracts/chapter-editor-bridge.md — the <c>setDistractionFree</c> contract: both toggle
/// routes work, and neither cursor nor content is disturbed by the transition.
/// </summary>
public class DistractionFreeBridgeTests
{
    [Fact]
    public async Task Entering_focus_mode_tells_the_editor_to_hide_its_chrome()
    {
        await using var app = new AppHarness();
        await app.OpenNewChapterAsync();

        await app.Editor.ToggleDistractionFreeAsync();

        Assert.True(app.Editor.IsDistractionFree);
        Assert.True(app.EditorHost.IsDistractionFree);
        Assert.Contains("setDistractionFree:True", app.EditorHost.Calls);
    }

    [Fact]
    public async Task Leaving_focus_mode_restores_the_chrome()
    {
        await using var app = new AppHarness();
        await app.OpenNewChapterAsync();

        await app.Editor.ToggleDistractionFreeAsync();
        await app.Editor.ToggleDistractionFreeAsync();

        Assert.False(app.Editor.IsDistractionFree);
        Assert.False(app.EditorHost.IsDistractionFree);
        Assert.Contains("setDistractionFree:False", app.EditorHost.Calls);
    }

    [Fact]
    public async Task The_visible_control_and_the_keyboard_shortcut_do_the_same_thing()
    {
        // FR-008 requires both routes. They converge on one command inside the view model, so this
        // asserts that convergence rather than two parallel implementations.
        await using var app = new AppHarness();
        await app.OpenNewChapterAsync();

        // Route 1: the visible control.
        await app.Editor.ToggleDistractionFreeCommand.ExecuteAsync(null);
        Assert.True(app.Editor.IsDistractionFree);

        // Route 2: the in-editor keyboard shortcut.
        app.EditorHost.PressFocusModeShortcut();
        await WaitUntilAsync(() => !app.Editor.IsDistractionFree);
        Assert.False(app.Editor.IsDistractionFree);

        app.EditorHost.PressFocusModeShortcut();
        await WaitUntilAsync(() => app.Editor.IsDistractionFree);
        Assert.True(app.Editor.IsDistractionFree);
    }

    [Fact]
    public async Task The_transition_never_replaces_the_document()
    {
        // The whole reason focus mode is a CSS class rather than a re-render: replacing the
        // document would lose the undo history and could lose text.
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();
        app.EditorHost.Type(chapterId, "The snow came early that year.");
        int replacementsBefore = app.EditorHost.DocumentReplacements;

        await app.Editor.ToggleDistractionFreeAsync();
        await app.Editor.ToggleDistractionFreeAsync();

        Assert.Equal(replacementsBefore, app.EditorHost.DocumentReplacements);
        Assert.Equal("The snow came early that year.", app.EditorHost.Document);
    }

    [Fact]
    public async Task The_caret_is_restored_on_every_transition()
    {
        // US2 scenario 3: the writer returns to their exact cursor position.
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();
        app.EditorHost.Type(chapterId, "The snow came early that year.");
        app.EditorHost.PlaceCaret(9);

        await app.Editor.ToggleDistractionFreeAsync();
        Assert.Equal(9, app.EditorHost.Caret);

        await app.Editor.ToggleDistractionFreeAsync();
        Assert.Equal(9, app.EditorHost.Caret);

        // Focus is handed back explicitly after each transition rather than left to the layout
        // change, which is what makes this true on all three WebView engines.
        Assert.Equal(2, app.EditorHost.FocusRestoreCount);
    }

    [Fact]
    public async Task Focus_is_restored_after_the_layout_change_not_before()
    {
        await using var app = new AppHarness();
        await app.OpenNewChapterAsync();
        app.EditorHost.Calls.Clear();

        await app.Editor.ToggleDistractionFreeAsync();

        int layout = app.EditorHost.Calls.IndexOf("setDistractionFree:True");
        int focus = app.EditorHost.Calls.IndexOf("focusEditor");
        Assert.True(layout >= 0 && focus > layout, "the caret must be restored after the chrome moves");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }
}
