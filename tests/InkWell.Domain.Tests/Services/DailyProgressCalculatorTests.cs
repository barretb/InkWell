using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;
using InkWell.Domain.Services;

namespace InkWell.Domain.Tests.Services;

/// <summary>
/// FR-011, FR-012, SC-005 — the numbers behind the progress display, and the day-boundary rule that
/// decides which day a word belongs to.
/// </summary>
public class DailyProgressCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 3, 14);

    [Fact]
    public void Two_hundred_of_five_hundred_reads_as_forty_percent_in_progress()
    {
        // The spec's worked example (US3 independent test).
        DailyProgressSnapshot progress = DailyProgressCalculator.ForDay(Record(200), Goal(500));

        Assert.Equal(200, progress.WordsWritten);
        Assert.Equal(500, progress.Target);
        Assert.Equal(300, progress.Remaining);
        Assert.Equal(GoalStatus.InProgress, progress.Status);
        Assert.Equal(0.4, progress.Fraction!.Value, precision: 6);
    }

    [Fact]
    public void Another_three_hundred_words_meets_the_goal()
    {
        DailyProgressSnapshot progress = DailyProgressCalculator.ForDay(Record(500), Goal(500));

        Assert.Equal(0, progress.Remaining);
        Assert.Equal(GoalStatus.Met, progress.Status);
        Assert.Equal(1.0, progress.Fraction!.Value, precision: 6);
    }

    [Fact]
    public void Writing_past_the_goal_reads_as_exceeded_without_overflowing_the_bar()
    {
        DailyProgressSnapshot progress = DailyProgressCalculator.ForDay(Record(900), Goal(500));

        Assert.Equal(GoalStatus.Exceeded, progress.Status);
        Assert.Equal(0, progress.Remaining);
        Assert.Equal(1.0, progress.Fraction!.Value, precision: 6);
    }

    [Fact]
    public void A_day_with_no_record_is_zero_words_not_a_missing_value()
    {
        DailyProgressSnapshot progress = DailyProgressCalculator.ForDay(record: null, Goal(500));

        Assert.Equal(0, progress.WordsWritten);
        Assert.Equal(500, progress.Remaining);
        Assert.Equal(GoalStatus.InProgress, progress.Status);
    }

    [Fact]
    public void Without_a_goal_there_is_a_word_count_but_no_progress()
    {
        DailyProgressSnapshot progress = DailyProgressCalculator.ForDay(Record(200), goal: null);

        Assert.Equal(200, progress.WordsWritten);
        Assert.Null(progress.Target);
        Assert.Null(progress.Fraction);
        Assert.Equal(GoalStatus.NoGoal, progress.Status);
    }

    [Fact]
    public void A_cleared_goal_stops_counting_as_a_goal_but_the_words_still_show()
    {
        // FR-010: clearing deactivates the goal and keeps the history.
        var cleared = new DailyGoal { TargetWords = 500, IsActive = false };

        DailyProgressSnapshot progress = DailyProgressCalculator.ForDay(Record(200), cleared);

        Assert.Equal(200, progress.WordsWritten);
        Assert.Null(progress.Target);
        Assert.Equal(GoalStatus.NoGoal, progress.Status);
    }

    [Fact]
    public void Words_belong_to_the_local_calendar_day_they_were_typed_on()
    {
        var lateEvening = new DateTimeOffset(2026, 3, 14, 23, 59, 0, TimeSpan.FromHours(2));
        var justAfterMidnight = new DateTimeOffset(2026, 3, 15, 0, 1, 0, TimeSpan.FromHours(2));

        Assert.Equal(new DateOnly(2026, 3, 14), DailyProgressCalculator.LocalDayOf(lateEvening));
        Assert.Equal(new DateOnly(2026, 3, 15), DailyProgressCalculator.LocalDayOf(justAfterMidnight));
    }

    [Fact]
    public void The_day_boundary_follows_the_devices_offset_not_utc()
    {
        // 01:30 on the 15th at UTC+2 is still 23:30 on the 14th in UTC. Using UTC would file these
        // words under the previous day for anyone east of Greenwich (spec.md §Assumptions).
        var earlyHoursInBerlin = new DateTimeOffset(2026, 3, 15, 1, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal(new DateOnly(2026, 3, 15), DailyProgressCalculator.LocalDayOf(earlyHoursInBerlin));
        Assert.Equal(new DateOnly(2026, 3, 14), DateOnly.FromDateTime(earlyHoursInBerlin.UtcDateTime));
    }

    [Fact]
    public void The_answer_does_not_depend_on_the_machines_own_time_zone()
    {
        // A pure function of its argument: the same instant must classify the same way whatever
        // zone the test host is configured for.
        var atOffset = new DateTimeOffset(2026, 3, 14, 23, 59, 0, TimeSpan.FromHours(9));

        Assert.Equal(new DateOnly(2026, 3, 14), DailyProgressCalculator.LocalDayOf(atOffset));
    }

    [Fact]
    public void A_new_day_starts_at_zero_while_the_target_survives()
    {
        // US3 scenario 4: yesterday's 500 words do not carry over, but the goal does.
        DailyWritingRecord yesterday = Record(500, Today.AddDays(-1));
        DailyGoal goal = Goal(500);

        DailyProgressSnapshot todayProgress = DailyProgressCalculator.ForDay(record: null, goal);

        Assert.Equal(0, todayProgress.WordsWritten);
        Assert.Equal(500, todayProgress.Target);
        Assert.Equal(GoalStatus.InProgress, todayProgress.Status);
        Assert.Equal(500, yesterday.WordsWritten);
    }

    [Theory]
    [InlineData(0, 12, 12)]      // first words of the day
    [InlineData(120, 150, 30)]   // wrote thirty more
    [InlineData(150, 150, 0)]    // saved with no change
    [InlineData(150, 120, -30)]  // deleted thirty
    public void The_days_total_moves_by_the_change_in_the_chapters_count(int before, int after, int expected)
        => Assert.Equal(expected, DailyProgressCalculator.WordsDelta(before, after));

    [Theory]
    [InlineData(200, 50, 250)]
    [InlineData(200, -50, 150)]
    [InlineData(20, -50, 0)]     // deleting more than was written today cannot go negative
    [InlineData(0, -10, 0)]
    public void A_days_total_never_goes_negative(int existing, int delta, int expected)
    {
        // data-model.md: WordsWritten ≥ 0. Deleting yesterday's prose today must not produce a
        // negative day, which would then have to be explained to the writer.
        Assert.Equal(expected, DailyProgressCalculator.ApplyDelta(existing, delta));
    }

    private static DailyWritingRecord Record(int words, DateOnly? date = null) => new()
    {
        Id = Guid.NewGuid(),
        Date = date ?? Today,
        WordsWritten = words,
    };

    private static DailyGoal Goal(int target) => new()
    {
        Id = Guid.NewGuid(),
        TargetWords = target,
        IsActive = true,
    };
}
