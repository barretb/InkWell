namespace InkWell.Domain.Abstractions;

/// <summary>
/// How the writer is doing against today's word-count goal.
/// </summary>
/// <remarks>
/// This is a distinct enum rather than something the view infers from comparing numbers, because
/// FR-019 forbids conveying state by colour alone: every layer that shows progress has to be able
/// to name the state in words, and a shared enum is what keeps those words consistent.
/// <see cref="Met"/> and <see cref="Exceeded"/> are separate for the same reason — the spec's
/// boundary edge case asks for "met vs. exceeded" to be distinguishable, not merely "at least met".
/// </remarks>
public enum GoalStatus
{
    /// <summary>No active daily goal, so there is no progress to report.</summary>
    NoGoal = 0,

    /// <summary>Words written today, but fewer than the target.</summary>
    InProgress = 1,

    /// <summary>Words written today exactly equal the target.</summary>
    Met = 2,

    /// <summary>Words written today exceed the target.</summary>
    Exceeded = 3,
}
