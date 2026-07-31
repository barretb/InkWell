using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;

namespace InkWell.Domain.Services;

/// <summary>
/// Today's progress toward the daily goal.
/// </summary>
/// <param name="WordsWritten">Prose words attributed to the day.</param>
/// <param name="Target">The active target, or null when no goal is set.</param>
/// <param name="Remaining">Words still needed; never negative.</param>
/// <param name="Status">The met/exceeded/in-progress state.</param>
public readonly record struct DailyProgressSnapshot(int WordsWritten, int? Target, int Remaining, GoalStatus Status)
{
    /// <summary>
    /// Completion as a fraction of the target, or null when no goal is set.
    /// </summary>
    /// <remarks>
    /// Capped at 1.0 so a progress bar cannot render past its own width, while
    /// <see cref="Status"/> still reports <see cref="GoalStatus.Exceeded"/> — the overshoot is
    /// communicated in words rather than by a broken layout.
    /// </remarks>
    public double? Fraction => Target is { } target && target > 0
        ? Math.Min(1.0, (double)WordsWritten / target)
        : null;
}

/// <summary>
/// Works out which day words belong to and how far through today's goal the writer is
/// (FR-011, FR-012, SC-005).
/// </summary>
/// <remarks>
/// Every rule here is a pure function of values passed in, including the day boundary. That is
/// deliberate: "does writing after midnight count toward the new day?" is otherwise only testable
/// by waiting for midnight.
/// </remarks>
public static class DailyProgressCalculator
{
    /// <summary>
    /// The local calendar day an instant falls on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads the calendar day at the offset the instant itself carries, not UTC and not the
    /// process's ambient time zone. A writer at UTC+2 finishing at 23:30 has written on that
    /// evening's date, and would find their words filed under tomorrow if this used UTC
    /// (spec.md §Assumptions: "daily follows the device's local calendar day").
    /// </para>
    /// <para>
    /// Using <c>instant.DateTime</c> rather than <c>instant.LocalDateTime</c> is deliberate:
    /// <c>LocalDateTime</c> re-projects into whatever zone the process happens to be running in,
    /// which would make this function's answer depend on ambient state and would silently disagree
    /// with the offset <c>IClock</c> supplied.
    /// </para>
    /// </remarks>
    /// <param name="instant">When the words were typed, with the device's offset.</param>
    /// <returns>The calendar day at that offset.</returns>
    public static DateOnly LocalDayOf(DateTimeOffset instant) => DateOnly.FromDateTime(instant.DateTime);

    /// <summary>
    /// Builds the progress a screen shows for one day.
    /// </summary>
    /// <param name="record">That day's writing record, or null when nothing was written yet.</param>
    /// <param name="goal">The manuscript's goal, or null when none was ever set.</param>
    /// <returns>The day's progress.</returns>
    public static DailyProgressSnapshot ForDay(DailyWritingRecord? record, DailyGoal? goal)
    {
        int written = record?.WordsWritten ?? 0;

        // A cleared goal is not a target: the writer asked to stop being measured, so the words
        // still show but nothing is "in progress" (FR-010).
        int? target = goal is { IsActive: true } active && GoalEvaluator.IsValidTarget(active.TargetWords)
            ? active.TargetWords
            : null;

        return new DailyProgressSnapshot(
            written,
            target,
            GoalEvaluator.Remaining(written, target),
            GoalEvaluator.Evaluate(written, target));
    }

    /// <summary>
    /// How much a chapter save moves the day's total.
    /// </summary>
    /// <remarks>
    /// Progress is the change in a chapter's word count, not its absolute size — otherwise opening
    /// an existing 3,000-word chapter and saving it would credit the writer with 3,000 words they
    /// wrote last month. Deleting prose yields a negative delta, which is what "net prose words"
    /// means in data-model.md.
    /// </remarks>
    /// <param name="wordCountBefore">The chapter's stored word count before the save.</param>
    /// <param name="wordCountAfter">Its recomputed word count after the save.</param>
    /// <returns>The signed change.</returns>
    public static int WordsDelta(int wordCountBefore, int wordCountAfter) => wordCountAfter - wordCountBefore;

    /// <summary>
    /// Applies a delta to a day's running total.
    /// </summary>
    /// <remarks>
    /// Clamped at zero per data-model.md. A writer who spends the morning cutting yesterday's
    /// chapter would otherwise see a negative day, which reads as a bug rather than as honesty.
    /// </remarks>
    /// <param name="existingWords">The day's total so far.</param>
    /// <param name="delta">The signed change from one save.</param>
    /// <returns>The new total, never negative.</returns>
    public static int ApplyDelta(int existingWords, int delta) => Math.Max(0, existingWords + delta);
}
