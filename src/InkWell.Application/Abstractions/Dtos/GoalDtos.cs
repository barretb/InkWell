using InkWell.Domain.Abstractions;
using InkWell.Domain.Services;

namespace InkWell.Application.Abstractions.Dtos;

/// <summary>
/// Today's progress toward the daily word-count goal, plus the wording the UI shows for it
/// (FR-011, FR-019).
/// </summary>
/// <param name="WordsWritten">Prose words attributed to the current local day.</param>
/// <param name="Target">The active target, or null when no goal is set.</param>
/// <param name="Remaining">Words still needed, never negative.</param>
/// <param name="Status">The met/exceeded/in-progress state.</param>
public sealed record DailyProgress(int WordsWritten, int? Target, int Remaining, GoalStatus Status)
{
    /// <summary>Completion as a fraction of the target, capped at 1.0; null when no goal is set.</summary>
    public double? Fraction => Target is > 0 ? Math.Min(1.0, (double)WordsWritten / Target.Value) : null;

    /// <summary>The status as a phrase, so it is never conveyed by appearance alone (FR-019).</summary>
    public string StatusText => GoalEvaluator.Describe(Status);

    /// <summary>
    /// The whole progress line in one string, for the editor's status area and for screen-reader
    /// announcement.
    /// </summary>
    public string Summary => Target is { } target
        ? $"Today: {WordsWritten:N0} of {target:N0} words · {Remaining:N0} to go · {StatusText}"
        : $"Today: {WordsWritten:N0} words · {StatusText}";

    /// <summary>Lifts a domain snapshot into the DTO the presentation layer binds to.</summary>
    public static DailyProgress From(DailyProgressSnapshot snapshot)
        => new(snapshot.WordsWritten, snapshot.Target, snapshot.Remaining, snapshot.Status);

    /// <summary>Progress with no goal set.</summary>
    public static DailyProgress None(int wordsWritten)
        => new(wordsWritten, null, 0, GoalStatus.NoGoal);
}

/// <summary>
/// One day of the writing history, as the history list shows it (FR-012).
/// </summary>
/// <param name="Date">The local calendar day.</param>
/// <param name="WordsWritten">Prose words written that day.</param>
/// <param name="GoalTarget">The target that applied that day, if any.</param>
/// <param name="GoalMet">Whether that day's target was reached.</param>
public sealed record WritingHistoryEntry(DateOnly Date, int WordsWritten, int? GoalTarget, bool GoalMet)
{
    /// <summary>
    /// The day's outcome in words. History is as subject to FR-019 as live progress is, so a met
    /// day is never distinguished from a missed one by colour alone.
    /// </summary>
    public string Outcome => GoalTarget is { } target
        ? GoalMet
            ? $"{WordsWritten:N0} of {target:N0} words · Goal met"
            : $"{WordsWritten:N0} of {target:N0} words · Goal not met"
        : $"{WordsWritten:N0} words · No goal that day";
}
