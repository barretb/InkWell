using InkWell.Application.Abstractions.Dtos;
using InkWell.Application.UseCases;
using InkWell.Domain.Abstractions;
using InkWell.Maui.UiTests.Harness;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.UiTests.Accessibility;

/// <summary>
/// US3 · FR-019, SC-007 — goal progress is stated in words, never by colour or by a bar alone, and
/// every status the writer can reach is distinguishable in text.
/// </summary>
public class UserStory3AccessibilityTests
{
    [Theory]
    [InlineData(0, 500, "In progress")]
    [InlineData(200, 500, "In progress")]
    [InlineData(500, 500, "Goal met")]
    [InlineData(900, 500, "Goal exceeded")]
    public void Every_reachable_status_has_its_own_words(int written, int target, string expected)
    {
        DailyProgress progress = GoalUseCases.ProgressFrom(written, target);

        Assert.Equal(expected, progress.StatusText);
        Assert.Contains(expected, progress.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summary_carries_every_number_the_progress_bar_shows()
    {
        // The bar is decoration. If it vanished — greyscale, a screen reader, a stylesheet failing
        // to load — this sentence must still say everything (FR-019).
        DailyProgress progress = GoalUseCases.ProgressFrom(200, 500);

        Assert.Contains("200", progress.Summary, StringComparison.Ordinal);
        Assert.Contains("500", progress.Summary, StringComparison.Ordinal);
        Assert.Contains("300 to go", progress.Summary, StringComparison.Ordinal);
        Assert.Contains("In progress", progress.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Met_and_exceeded_are_never_the_same_words()
    {
        // The spec's boundary edge case asks for these to be distinguishable, not merely both
        // "done" — a writer who overshot should be able to tell.
        string met = GoalUseCases.ProgressFrom(500, 500).StatusText;
        string exceeded = GoalUseCases.ProgressFrom(501, 500).StatusText;

        Assert.NotEqual(met, exceeded);
    }

    [Fact]
    public async Task The_goals_screen_announces_progress_as_text()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();
        await app.GoalUseCases.SetGoalAsync(manuscriptId, 500);
        app.EditorHost.Replace(chapterId, string.Join(' ', Enumerable.Repeat("word", 200)));
        await app.Editor.FlushAsync();

        GoalsViewModel goals = app.Goals;
        goals.ManuscriptId = manuscriptId;
        await goals.LoadAsync();

        Assert.Contains("200", goals.ProgressSummary, StringComparison.Ordinal);
        Assert.Contains("300 to go", goals.ProgressSummary, StringComparison.Ordinal);
        Assert.Equal("In progress", goals.StatusText);
        Assert.Equal(goals.ProgressSummary, goals.StatusMessage);
    }

    [Fact]
    public async Task With_no_goal_the_screen_says_so_rather_than_showing_an_empty_bar()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        GoalsViewModel goals = app.Goals;
        goals.ManuscriptId = manuscriptId;
        await goals.LoadAsync();

        Assert.False(goals.HasActiveGoal);
        Assert.Equal(0d, goals.ProgressFraction);
        Assert.Equal("No daily goal set", goals.StatusText);
    }

    [Fact]
    public async Task An_empty_history_offers_guidance_instead_of_a_void()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        GoalsViewModel goals = app.Goals;
        goals.ManuscriptId = manuscriptId;
        await goals.LoadAsync();

        Assert.True(goals.HasNoHistory);
    }

    [Fact]
    public void A_past_day_states_its_outcome_in_words()
    {
        var met = new WritingHistoryEntry(new DateOnly(2026, 3, 14), 500, 500, GoalMet: true);
        var missed = new WritingHistoryEntry(new DateOnly(2026, 3, 15), 120, 500, GoalMet: false);
        var noGoal = new WritingHistoryEntry(new DateOnly(2026, 3, 16), 300, null, GoalMet: false);

        Assert.Contains("Goal met", met.Outcome, StringComparison.Ordinal);
        Assert.Contains("Goal not met", missed.Outcome, StringComparison.Ordinal);
        Assert.Contains("No goal that day", noGoal.Outcome, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_editor_carries_the_goal_line_so_progress_is_reachable_while_writing()
    {
        // FR-015's spirit applied to goals: the writer should not have to leave the editor to know
        // where they stand, and a screen-reader user should hear it from the same status region.
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();
        await app.GoalUseCases.SetGoalAsync(manuscriptId, 500);
        await app.Editor.LoadAsync();

        app.EditorHost.Replace(chapterId, string.Join(' ', Enumerable.Repeat("word", 120)));
        await app.Editor.FlushAsync();

        Assert.True(app.Editor.HasDailyGoal);
        Assert.Contains("120", app.Editor.GoalSummary, StringComparison.Ordinal);
        Assert.Contains("380 to go", app.Editor.GoalSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_goal_the_editor_shows_no_goal_line_at_all()
    {
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();

        app.EditorHost.Replace(chapterId, "Some prose.");
        await app.Editor.FlushAsync();

        Assert.False(app.Editor.HasDailyGoal);
        Assert.Equal(GoalStatus.NoGoal, app.Editor.TodayProgress!.Status);
    }
}
