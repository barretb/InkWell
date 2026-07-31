using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Abstractions;
using InkWell.Maui.UiTests.Harness;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.UiTests;

/// <summary>
/// User Story 3 end to end: "Set a daily goal of 500 words, write 200 words, confirm progress shows
/// 200/500 (40%), write 300 more, and confirm the goal is marked met for the day."
/// </summary>
public class UserStory3Tests
{
    [Fact]
    public async Task The_writer_sets_a_goal_writes_toward_it_and_sees_it_met()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();

        // 1. Set a daily target; tracking begins (US3 scenario 1).
        GoalsViewModel goals = app.Goals;
        goals.ManuscriptId = manuscriptId;
        goals.TargetInput = "500";
        await goals.FlushAsync();
        await goals.LoadAsync();

        Assert.True(goals.HasActiveGoal);
        Assert.Equal(500, goals.Progress!.Target);

        // 2. Write 200 words → 200/500, 40%, 300 remaining, in progress (US3 scenario 2).
        await TypeWordsAsync(app, chapterId, 200);
        await goals.LoadAsync();

        Assert.Equal(200, goals.Progress!.WordsWritten);
        Assert.Equal(300, goals.Progress.Remaining);
        Assert.Equal(0.4, goals.ProgressFraction, precision: 6);
        Assert.Equal(GoalStatus.InProgress, goals.Progress.Status);

        // 3. Write 300 more → the goal is met for the day (US3 scenario 3).
        await TypeWordsAsync(app, chapterId, 500);
        await goals.LoadAsync();

        Assert.Equal(500, goals.Progress!.WordsWritten);
        Assert.Equal(0, goals.Progress.Remaining);
        Assert.Equal(GoalStatus.Met, goals.Progress.Status);
        Assert.Equal("Goal met", goals.StatusText);
    }

    [Fact]
    public async Task The_editor_shows_progress_updating_as_the_writer_types()
    {
        // US3 scenario 2 from the writer's seat: the number moves while they are in the editor,
        // without them going and looking for it.
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();
        await app.GoalUseCases.SetGoalAsync(manuscriptId, 500);
        await app.Editor.LoadAsync();

        await TypeWordsAsync(app, chapterId, 200);

        Assert.Equal(200, app.Editor.TodayProgress!.WordsWritten);
        Assert.Equal(500, app.Editor.TodayProgress.Target);
        Assert.True(app.Editor.HasDailyGoal);
        Assert.Contains("300 to go", app.Editor.GoalSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Progress_counts_prose_only_not_markdown_or_images()
    {
        // SC-005: the progress the writer is measured against must match a reader's word count.
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();
        await app.GoalUseCases.SetGoalAsync(manuscriptId, 100);
        await app.Editor.LoadAsync();

        app.EditorHost.Type(
            chapterId,
            "## Chapter One\n\nHe was **never** coming back. ![a photograph of the mill](inkwell-img://abc)");
        await app.Editor.FlushAsync();

        // "Chapter One" (2) + "He was never coming back." (5). Emphasis and the image count zero.
        Assert.Equal(7, app.Editor.ChapterWordCount);
        Assert.Equal(7, app.Editor.TodayProgress!.WordsWritten);
    }

    [Fact]
    public async Task Writing_past_the_goal_reads_as_exceeded()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();
        await app.GoalUseCases.SetGoalAsync(manuscriptId, 100);

        await TypeWordsAsync(app, chapterId, 250);

        DailyProgress progress = await app.GoalUseCases.GetTodayProgressAsync(manuscriptId);
        Assert.Equal(GoalStatus.Exceeded, progress.Status);
        Assert.Equal(0, progress.Remaining);
        Assert.Equal("Goal exceeded", progress.StatusText);
    }

    [Fact]
    public async Task A_new_day_resets_progress_and_keeps_yesterday_in_the_history()
    {
        // US3 scenario 4 / FR-012 — the rollover the writer sees on reopening the app.
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();
        await app.GoalUseCases.SetGoalAsync(manuscriptId, 500);
        await TypeWordsAsync(app, chapterId, 500);

        DateOnly yesterday = app.Clock.Today;
        app.Clock.AdvancePastMidnight();

        GoalsViewModel goals = app.Goals;
        goals.ManuscriptId = manuscriptId;
        await goals.LoadAsync();

        Assert.Equal(0, goals.Progress!.WordsWritten);
        Assert.Equal(500, goals.Progress.Target);
        Assert.Equal(GoalStatus.InProgress, goals.Progress.Status);

        WritingHistoryEntry prior = Assert.Single(goals.History, entry => entry.Date == yesterday);
        Assert.Equal(500, prior.WordsWritten);
        Assert.True(prior.GoalMet);
        Assert.Contains("Goal met", prior.Outcome, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Words_typed_after_midnight_count_toward_the_new_day()
    {
        // The mid-session rollover edge case: the app is never closed, the day still changes.
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();
        await app.GoalUseCases.SetGoalAsync(manuscriptId, 500);
        await TypeWordsAsync(app, chapterId, 400);

        app.Clock.AdvancePastMidnight();
        await TypeWordsAsync(app, chapterId, 450);

        DailyProgress today = await app.GoalUseCases.GetTodayProgressAsync(manuscriptId);
        Assert.Equal(50, today.WordsWritten);
        Assert.Equal(2, (await app.GoalUseCases.GetHistoryAsync(manuscriptId)).Count);
    }

    [Fact]
    public async Task Clearing_the_goal_stops_tracking_but_keeps_the_history()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();
        await app.GoalUseCases.SetGoalAsync(manuscriptId, 500);
        await TypeWordsAsync(app, chapterId, 200);

        GoalsViewModel goals = app.Goals;
        goals.ManuscriptId = manuscriptId;
        await goals.LoadAsync();
        app.Confirmation.NextAnswer = true;
        await goals.ClearGoalAsync();

        Assert.False(goals.HasActiveGoal);
        Assert.Equal(200, goals.Progress!.WordsWritten);
        Assert.Single(goals.History);
        Assert.Contains("history is kept", goals.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Declining_the_confirmation_leaves_the_goal_alone()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();
        await app.GoalUseCases.SetGoalAsync(manuscriptId, 500);

        GoalsViewModel goals = app.Goals;
        goals.ManuscriptId = manuscriptId;
        await goals.LoadAsync();
        app.Confirmation.NextAnswer = false;
        await goals.ClearGoalAsync();

        Assert.True(goals.HasActiveGoal);
        Assert.Equal(500, goals.Progress!.Target);
    }

    [Fact]
    public async Task A_target_that_is_not_a_number_is_explained_without_a_dialog()
    {
        // There is no save button, so this runs while the writer may still be typing. Interrupting
        // them with a modal over a half-typed value would be worse than the value not saving.
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        GoalsViewModel goals = app.Goals;
        goals.ManuscriptId = manuscriptId;
        goals.TargetInput = "five hundred";
        await goals.FlushAsync();

        Assert.Empty(app.Errors.Errors);
        Assert.Contains("Not saved", goals.StatusMessage, StringComparison.Ordinal);
        Assert.False(goals.HasActiveGoal);
    }

    [Fact]
    public async Task A_target_of_zero_is_rejected()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        GoalsViewModel goals = app.Goals;
        goals.ManuscriptId = manuscriptId;
        goals.TargetInput = "0";
        await goals.FlushAsync();

        Assert.Contains("at least one word", goals.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(goals.HasActiveGoal);
        Assert.Null(await app.GoalUseCases.GetGoalAsync(manuscriptId));
    }

    [Fact]
    public async Task The_goal_and_history_survive_a_restart()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();
        await app.GoalUseCases.SetGoalAsync(manuscriptId, 750);
        await TypeWordsAsync(app, chapterId, 310);

        await app.RestartAsync();

        GoalsViewModel goals = app.Goals;
        goals.ManuscriptId = manuscriptId;
        await goals.LoadAsync();

        Assert.Equal(750, goals.Progress!.Target);
        Assert.Equal(310, goals.Progress.WordsWritten);
        Assert.Equal("750", goals.TargetInput);
    }

    /// <summary>
    /// Writes until the chapter holds exactly <paramref name="totalWords"/> prose words, through
    /// the editor and its autosave — the same path the writer takes.
    /// </summary>
    private static async Task TypeWordsAsync(AppHarness app, Guid chapterId, int totalWords)
    {
        string markdown = totalWords == 0 ? string.Empty : string.Join(' ', Enumerable.Repeat("word", totalWords));
        app.EditorHost.Replace(chapterId, markdown);
        await app.Editor.FlushAsync();
    }
}
