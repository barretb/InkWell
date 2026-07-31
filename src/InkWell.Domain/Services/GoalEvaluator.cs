using InkWell.Domain.Abstractions;

namespace InkWell.Domain.Services;

/// <summary>
/// Decides where the writer stands against today's target (FR-011).
/// </summary>
/// <remarks>
/// Kept separate from <see cref="DailyProgressCalculator"/> because this is the part that has to
/// agree everywhere — the editor's status line, the goals screen, and the snapshot written into the
/// writing history all ask the same question and must get the same answer.
/// </remarks>
public static class GoalEvaluator
{
    /// <summary>Whether a number is usable as a daily target.</summary>
    /// <param name="targetWords">The target the writer entered.</param>
    /// <returns>True when the target is at least one word.</returns>
    public static bool IsValidTarget(int targetWords) => targetWords > 0;

    /// <summary>
    /// Classifies today's progress.
    /// </summary>
    /// <param name="wordsWritten">Prose words attributed to the current local day.</param>
    /// <param name="activeTargetWords">The active target, or null when no goal is set.</param>
    /// <returns>The status the UI must name in words.</returns>
    public static GoalStatus Evaluate(int wordsWritten, int? activeTargetWords)
    {
        if (activeTargetWords is not { } target || !IsValidTarget(target))
        {
            return GoalStatus.NoGoal;
        }

        if (wordsWritten > target)
        {
            return GoalStatus.Exceeded;
        }

        return wordsWritten == target ? GoalStatus.Met : GoalStatus.InProgress;
    }

    /// <summary>
    /// How many words are still needed today.
    /// </summary>
    /// <param name="wordsWritten">Prose words attributed to the current local day.</param>
    /// <param name="activeTargetWords">The active target, or null when no goal is set.</param>
    /// <returns>Never negative; zero once the goal is reached or passed.</returns>
    public static int Remaining(int wordsWritten, int? activeTargetWords)
        => activeTargetWords is { } target && IsValidTarget(target)
            ? Math.Max(0, target - wordsWritten)
            : 0;

    /// <summary>
    /// The phrase that names a status.
    /// </summary>
    /// <remarks>
    /// Lives here rather than in the view so that the editor, the goals screen, and any future
    /// surface cannot describe the same state differently — and so that a status can never reach
    /// the writer as colour with no words attached (FR-019).
    /// </remarks>
    /// <param name="status">The status to describe.</param>
    /// <returns>A short human-readable phrase.</returns>
    public static string Describe(GoalStatus status) => status switch
    {
        GoalStatus.InProgress => "In progress",
        GoalStatus.Met => "Goal met",
        GoalStatus.Exceeded => "Goal exceeded",
        _ => "No daily goal set",
    };
}
