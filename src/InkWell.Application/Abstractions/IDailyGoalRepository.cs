using InkWell.Domain.Entities;

namespace InkWell.Application.Abstractions;

/// <summary>
/// Storage for the single daily word-count goal a manuscript may carry
/// (contracts/word-count-and-goals.md).
/// </summary>
public interface IDailyGoalRepository
{
    /// <summary>Loads the manuscript's goal, or null when none was ever set.</summary>
    Task<DailyGoal?> GetAsync(Guid manuscriptId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or replaces the manuscript's goal and marks it active. Exactly one goal row exists per
    /// manuscript, so setting a new target updates the existing row rather than accumulating goals.
    /// </summary>
    Task<DailyGoal> SetAsync(
        Guid manuscriptId,
        int targetWords,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates the goal while keeping the row and all writing history (FR-010). History stays
    /// because a cleared goal must not erase the record of days already achieved.
    /// </summary>
    /// <returns>False when the manuscript has no goal.</returns>
    Task<bool> ClearAsync(Guid manuscriptId, DateTimeOffset timestamp, CancellationToken cancellationToken = default);
}
