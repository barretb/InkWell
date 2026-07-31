using InkWell.Domain.Abstractions;
using InkWell.Domain.Services;

namespace InkWell.Domain.Tests.Services;

/// <summary>
/// FR-011 and the spec's word-count boundary edge case: progress exactly at the goal and progress
/// past it must both be handled clearly, and distinguishably.
/// </summary>
public class GoalEvaluatorTests
{
    [Theory]
    [InlineData(0, 500, GoalStatus.InProgress)]
    [InlineData(1, 500, GoalStatus.InProgress)]
    [InlineData(200, 500, GoalStatus.InProgress)]
    [InlineData(499, 500, GoalStatus.InProgress)]
    [InlineData(500, 500, GoalStatus.Met)]
    [InlineData(501, 500, GoalStatus.Exceeded)]
    [InlineData(5_000, 500, GoalStatus.Exceeded)]
    public void Status_distinguishes_in_progress_met_and_exceeded(int written, int target, GoalStatus expected)
        => Assert.Equal(expected, GoalEvaluator.Evaluate(written, target));

    [Fact]
    public void No_target_means_no_goal_however_much_was_written()
    {
        Assert.Equal(GoalStatus.NoGoal, GoalEvaluator.Evaluate(0, null));
        Assert.Equal(GoalStatus.NoGoal, GoalEvaluator.Evaluate(1_200, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_target_is_not_a_goal(int target)
        => Assert.Equal(GoalStatus.NoGoal, GoalEvaluator.Evaluate(100, target));

    [Theory]
    [InlineData(200, 500, 300)]
    [InlineData(0, 500, 500)]
    [InlineData(500, 500, 0)]
    [InlineData(900, 500, 0)]
    public void Remaining_never_goes_negative(int written, int target, int expected)
        => Assert.Equal(expected, GoalEvaluator.Remaining(written, target));

    [Fact]
    public void Remaining_is_zero_when_no_goal_is_set()
        => Assert.Equal(0, GoalEvaluator.Remaining(200, null));

    [Fact]
    public void A_target_is_only_valid_above_zero()
    {
        Assert.True(GoalEvaluator.IsValidTarget(1));
        Assert.True(GoalEvaluator.IsValidTarget(500));
        Assert.False(GoalEvaluator.IsValidTarget(0));
        Assert.False(GoalEvaluator.IsValidTarget(-10));
    }

    [Theory]
    [InlineData(GoalStatus.NoGoal, "No daily goal set")]
    [InlineData(GoalStatus.InProgress, "In progress")]
    [InlineData(GoalStatus.Met, "Goal met")]
    [InlineData(GoalStatus.Exceeded, "Goal exceeded")]
    public void Every_status_has_words_of_its_own(GoalStatus status, string expected)
    {
        // FR-019: the state must survive greyscale and a screen reader, so each one carries a
        // distinct phrase rather than sharing "done" or relying on a tick.
        Assert.Equal(expected, GoalEvaluator.Describe(status));
    }
}
