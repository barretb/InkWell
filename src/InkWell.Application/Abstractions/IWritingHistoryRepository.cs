using InkWell.Domain.Entities;

namespace InkWell.Application.Abstractions;

/// <summary>
/// Storage for the per-day writing history that daily progress and the history view read from
/// (contracts/word-count-and-goals.md). One row per manuscript per local calendar day.
/// </summary>
public interface IWritingHistoryRepository
{
    /// <summary>Loads the record for one day, or null when nothing was written that day.</summary>
    Task<DailyWritingRecord?> GetAsync(
        Guid manuscriptId,
        DateOnly localDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="deltaWords"/> to the given day's total, creating the row if this is the
    /// first writing of the day. Called from autosave, so a day boundary crossed mid-session simply
    /// starts writing to a new row (FR-012, US3 scenario 4).
    /// </summary>
    /// <returns>The day's total after the update.</returns>
    Task<DailyWritingRecord> AddWordsAsync(
        Guid manuscriptId,
        DateOnly localDate,
        int deltaWords,
        int? goalTarget,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists writing records between two dates inclusive, newest first — the writing history
    /// (FR-012).
    /// </summary>
    Task<IReadOnlyList<DailyWritingRecord>> ListAsync(
        Guid manuscriptId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);
}
