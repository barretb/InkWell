using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Tests.Fixtures;

namespace InkWell.Infrastructure.Tests.Persistence;

/// <summary>
/// US3 · contracts/word-count-and-goals.md — one goal per manuscript, one record per day, history
/// that survives the goal being changed or cleared.
/// </summary>
public class GoalAndHistoryRepositoryTests
{
    [Fact]
    public async Task Setting_a_goal_starts_tracking()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);

        DomainResult<DailyGoal> set = await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 500);

        Assert.True(set.IsSuccess);
        DailyProgress progress = await fixture.GoalUseCases.GetTodayProgressAsync(manuscript.Id);
        Assert.Equal(500, progress.Target);
        Assert.Equal(0, progress.WordsWritten);
        Assert.Equal(GoalStatus.InProgress, progress.Status);
    }

    [Fact]
    public async Task A_manuscript_never_accumulates_a_second_goal()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);

        await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 500);
        await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 750);

        DailyGoal? goal = await fixture.GoalUseCases.GetGoalAsync(manuscript.Id);
        Assert.Equal(750, goal!.TargetWords);
        Assert.True(goal.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_target_below_one_word_is_rejected(int target)
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);

        DomainResult<DailyGoal> set = await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, target);

        Assert.Equal(DomainErrorCode.ValidationError, set.Error.Code);
        Assert.Null(await fixture.GoalUseCases.GetGoalAsync(manuscript.Id));
    }

    [Fact]
    public async Task Clearing_a_goal_keeps_the_history_and_the_last_target()
    {
        // FR-010: clearing stops the measuring, it does not erase what was achieved.
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 500);
        await fixture.WriteWordsAsync(manuscript.Id, 200);

        DomainResult cleared = await fixture.GoalUseCases.ClearGoalAsync(manuscript.Id);

        Assert.True(cleared.IsSuccess);
        DailyGoal? goal = await fixture.GoalUseCases.GetGoalAsync(manuscript.Id);
        Assert.False(goal!.IsActive);
        Assert.Equal(500, goal.TargetWords);

        DailyProgress progress = await fixture.GoalUseCases.GetTodayProgressAsync(manuscript.Id);
        Assert.Equal(200, progress.WordsWritten);
        Assert.Null(progress.Target);
        Assert.Equal(GoalStatus.NoGoal, progress.Status);

        IReadOnlyList<WritingHistoryEntry> history = await fixture.GoalUseCases.GetHistoryAsync(manuscript.Id);
        Assert.Equal(200, Assert.Single(history).WordsWritten);
    }

    [Fact]
    public async Task Clearing_a_goal_that_was_never_set_reports_not_found()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);

        Assert.Equal(DomainErrorCode.NotFound, (await fixture.GoalUseCases.ClearGoalAsync(manuscript.Id)).Error.Code);
    }

    [Fact]
    public async Task Writing_accumulates_into_a_single_record_for_the_day()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 500);

        await fixture.WriteWordsAsync(manuscript.Id, 200);
        await fixture.WriteWordsAsync(manuscript.Id, 500);

        DailyProgress progress = await fixture.GoalUseCases.GetTodayProgressAsync(manuscript.Id);
        Assert.Equal(500, progress.WordsWritten);
        Assert.Equal(GoalStatus.Met, progress.Status);
        Assert.Single(await fixture.GoalUseCases.GetHistoryAsync(manuscript.Id));
    }

    [Fact]
    public async Task Deleting_prose_reduces_the_days_total_without_going_negative()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);

        await fixture.WriteWordsAsync(manuscript.Id, 300);
        await fixture.WriteWordsAsync(manuscript.Id, 100);

        DailyProgress progress = await fixture.GoalUseCases.GetTodayProgressAsync(manuscript.Id);
        Assert.Equal(100, progress.WordsWritten);
    }

    [Fact]
    public async Task Re_saving_an_unchanged_chapter_credits_nothing()
    {
        // Opening yesterday's 3,000-word chapter and saving it must not count as writing it again.
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        await fixture.WriteWordsAsync(manuscript.Id, 250);

        await fixture.WriteWordsAsync(manuscript.Id, 250);

        Assert.Equal(250, (await fixture.GoalUseCases.GetTodayProgressAsync(manuscript.Id)).WordsWritten);
    }

    [Fact]
    public async Task A_new_day_resets_progress_while_the_target_persists()
    {
        // US3 scenario 4 / FR-012 — the day-rollover requirement.
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 500);
        await fixture.WriteWordsAsync(manuscript.Id, 500);

        DateOnly yesterday = fixture.Clock.Today;
        fixture.Clock.AdvancePastMidnight();

        DailyProgress today = await fixture.GoalUseCases.GetTodayProgressAsync(manuscript.Id);
        Assert.Equal(0, today.WordsWritten);
        Assert.Equal(500, today.Target);
        Assert.Equal(GoalStatus.InProgress, today.Status);

        IReadOnlyList<WritingHistoryEntry> history = await fixture.GoalUseCases.GetHistoryAsync(manuscript.Id);
        WritingHistoryEntry prior = Assert.Single(history, entry => entry.Date == yesterday);
        Assert.Equal(500, prior.WordsWritten);
        Assert.True(prior.GoalMet);
    }

    [Fact]
    public async Task Words_typed_after_midnight_belong_to_the_new_day()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 500);
        await fixture.WriteWordsAsync(manuscript.Id, 400);

        DateOnly firstDay = fixture.Clock.Today;
        fixture.Clock.AdvancePastMidnight();
        await fixture.WriteWordsAsync(manuscript.Id, 460);

        DailyProgress today = await fixture.GoalUseCases.GetTodayProgressAsync(manuscript.Id);
        Assert.Equal(60, today.WordsWritten);

        IReadOnlyList<WritingHistoryEntry> history = await fixture.GoalUseCases.GetHistoryAsync(manuscript.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal(400, Assert.Single(history, e => e.Date == firstDay).WordsWritten);

        // Newest first (FR-012).
        Assert.Equal(fixture.Clock.Today, history[0].Date);
    }

    [Fact]
    public async Task Each_day_keeps_the_target_that_applied_on_it()
    {
        // Raising the goal tomorrow must not retroactively fail yesterday.
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 200);
        await fixture.WriteWordsAsync(manuscript.Id, 200);
        DateOnly firstDay = fixture.Clock.Today;

        fixture.Clock.AdvancePastMidnight();
        await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 1_000);
        await fixture.WriteWordsAsync(manuscript.Id, 300);

        IReadOnlyList<WritingHistoryEntry> history = await fixture.GoalUseCases.GetHistoryAsync(manuscript.Id);
        WritingHistoryEntry earlier = Assert.Single(history, e => e.Date == firstDay);
        Assert.Equal(200, earlier.GoalTarget);
        Assert.True(earlier.GoalMet);

        WritingHistoryEntry later = Assert.Single(history, e => e.Date == fixture.Clock.Today);
        Assert.Equal(1_000, later.GoalTarget);
        Assert.False(later.GoalMet);
    }

    [Fact]
    public async Task History_survives_a_restart()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 500);
        await fixture.WriteWordsAsync(manuscript.Id, 320);

        await fixture.RestartAsync();

        DailyProgress progress = await fixture.GoalUseCases.GetTodayProgressAsync(manuscript.Id);
        Assert.Equal(320, progress.WordsWritten);
        Assert.Equal(500, progress.Target);
        Assert.Equal(180, progress.Remaining);
    }

    [Fact]
    public async Task Deleting_a_manuscript_takes_its_goal_and_history_with_it()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 500);
        await fixture.WriteWordsAsync(manuscript.Id, 200);

        await fixture.ManuscriptUseCases.DeleteAsync(manuscript.Id);

        Assert.Null(await fixture.GoalUseCases.GetGoalAsync(manuscript.Id));
        Assert.Empty(await fixture.GoalUseCases.GetHistoryAsync(manuscript.Id));
    }

    [Fact]
    public async Task One_manuscripts_goal_does_not_affect_another()
    {
        await using var fixture = new StoreFixture();
        Manuscript first = await SeedAsync(fixture, "First");
        Manuscript second = await SeedAsync(fixture, "Second");
        await fixture.GoalUseCases.SetGoalAsync(first.Id, 500);

        await fixture.WriteWordsAsync(first.Id, 200);

        Assert.Equal(0, (await fixture.GoalUseCases.GetTodayProgressAsync(second.Id)).WordsWritten);
        Assert.Null((await fixture.GoalUseCases.GetTodayProgressAsync(second.Id)).Target);
    }

    private static async Task<Manuscript> SeedAsync(StoreFixture fixture, string title = "The Long Winter")
    {
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync(title)).Value;
        await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One");
        return manuscript;
    }
}
