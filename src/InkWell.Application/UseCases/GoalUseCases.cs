using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;
using InkWell.Domain.Services;

namespace InkWell.Application.UseCases;

/// <summary>
/// Setting, changing, and clearing the daily word-count goal, and reporting progress against it
/// (FR-010, FR-011, FR-012, contracts/word-count-and-goals.md).
/// </summary>
/// <remarks>
/// The contract also lists <c>RecordWordsForToday</c>. That operation is not exposed here on
/// purpose: recording a day's words happens inside the autosave transaction
/// (<see cref="IChapterRepository.CommitAutoSaveAsync"/>) so that prose and its day's total can
/// never diverge. A second, non-transactional write path for the same fact would be a way for them
/// to. <see cref="IWritingHistoryRepository.AddWordsAsync"/> remains available for tooling and
/// tests, and shares the same upsert.
/// </remarks>
public sealed class GoalUseCases
{
    private readonly IDailyGoalRepository _goals;
    private readonly IWritingHistoryRepository _history;
    private readonly IClock _clock;

    /// <summary>Creates the use cases.</summary>
    public GoalUseCases(IDailyGoalRepository goals, IWritingHistoryRepository history, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(clock);
        _goals = goals;
        _history = history;
        _clock = clock;
    }

    /// <summary>Loads the manuscript's goal, or null when none was ever set.</summary>
    public Task<DailyGoal?> GetGoalAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
        => _goals.GetAsync(manuscriptId, cancellationToken);

    /// <summary>
    /// Sets or changes the daily target, and starts tracking (US3 scenario 1).
    /// </summary>
    public async Task<DomainResult<DailyGoal>> SetGoalAsync(
        Guid manuscriptId,
        int targetWords,
        CancellationToken cancellationToken = default)
    {
        if (!GoalEvaluator.IsValidTarget(targetWords))
        {
            return DomainResult<DailyGoal>.Validation("A daily goal must be at least one word.");
        }

        DailyGoal goal = await _goals
            .SetAsync(manuscriptId, targetWords, _clock.Now, cancellationToken)
            .ConfigureAwait(false);

        return DomainResult<DailyGoal>.Success(goal);
    }

    /// <summary>Stops tracking against a target while keeping the writing history (FR-010).</summary>
    public async Task<DomainResult> ClearGoalAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        bool cleared = await _goals.ClearAsync(manuscriptId, _clock.Now, cancellationToken).ConfigureAwait(false);
        return cleared ? DomainResult.Success() : DomainResult.NotFound("This manuscript has no daily goal.");
    }

    /// <summary>
    /// Today's progress. "Today" is resolved from the clock on every call, so a session left open
    /// across midnight reports the new day rather than the one it started in (FR-012).
    /// </summary>
    public async Task<DailyProgress> GetTodayProgressAsync(
        Guid manuscriptId,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = _clock.Today;

        DailyGoal? goal = await _goals.GetAsync(manuscriptId, cancellationToken).ConfigureAwait(false);
        DailyWritingRecord? record = await _history
            .GetAsync(manuscriptId, today, cancellationToken)
            .ConfigureAwait(false);

        return DailyProgress.From(DailyProgressCalculator.ForDay(record, goal));
    }

    /// <summary>
    /// Prior days' results, newest first (FR-012).
    /// </summary>
    /// <param name="manuscriptId">The manuscript whose history to read.</param>
    /// <param name="days">How many days back to include, ending today.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<IReadOnlyList<WritingHistoryEntry>> GetHistoryAsync(
        Guid manuscriptId,
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = _clock.Today;
        DateOnly from = today.AddDays(-Math.Max(0, days - 1));

        IReadOnlyList<DailyWritingRecord> records = await _history
            .ListAsync(manuscriptId, from, today, cancellationToken)
            .ConfigureAwait(false);

        return [.. records.Select(r => new WritingHistoryEntry(r.Date, r.WordsWritten, r.GoalTarget, r.GoalMet))];
    }

    /// <summary>
    /// Builds progress from counts the caller already has, without touching the database.
    /// </summary>
    /// <remarks>
    /// The editor gets today's words and the active target back from every autosave commit, so it
    /// can refresh its progress line without a query on the keystroke path.
    /// </remarks>
    /// <param name="wordsWrittenToday">Words attributed to today, from the autosave result.</param>
    /// <param name="activeTarget">The active target, from the autosave result.</param>
    public static DailyProgress ProgressFrom(int wordsWrittenToday, int? activeTarget)
        => new(
            wordsWrittenToday,
            activeTarget,
            GoalEvaluator.Remaining(wordsWrittenToday, activeTarget),
            GoalEvaluator.Evaluate(wordsWrittenToday, activeTarget));
}
