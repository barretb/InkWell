using InkWell.Application.Abstractions;
using InkWell.Domain.Entities;
using SQLite;

namespace InkWell.Infrastructure.Persistence;

/// <summary>
/// SQLCipher-backed storage for the single daily goal a manuscript may carry (FR-010).
/// </summary>
public sealed class DailyGoalRepository : IDailyGoalRepository
{
    private readonly ISqliteConnectionFactory _factory;

    /// <summary>Creates the repository.</summary>
    public DailyGoalRepository(ISqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public async Task<DailyGoal?> GetAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<DailyGoalRow> rows = await connection
            .QueryAsync<DailyGoalRow>("SELECT * FROM DailyGoal WHERE ManuscriptId = ?", manuscriptId.ToString())
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0].ToEntity();
    }

    /// <inheritdoc />
    public async Task<DailyGoal> SetAsync(
        Guid manuscriptId,
        int targetWords,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        long ticks = RowConversions.ToTicks(timestamp);

        DailyGoal? result = null;

        await connection.RunInTransactionAsync(tx =>
        {
            List<DailyGoalRow> existing = tx.Query<DailyGoalRow>(
                "SELECT * FROM DailyGoal WHERE ManuscriptId = ?", manuscriptId.ToString());

            if (existing.Count > 0)
            {
                // Updated in place rather than inserted: exactly one goal exists per manuscript, so
                // raising the target is a change of mind, not a second goal to reconcile.
                tx.Execute(
                    "UPDATE DailyGoal SET TargetWords = ?, IsActive = 1, ModifiedAt = ? WHERE Id = ?",
                    targetWords, ticks, existing[0].Id);

                existing[0].TargetWords = targetWords;
                existing[0].IsActive = 1;
                existing[0].ModifiedAt = ticks;
                result = existing[0].ToEntity();
                return;
            }

            var row = new DailyGoalRow
            {
                Id = Guid.NewGuid().ToString(),
                ManuscriptId = manuscriptId.ToString(),
                TargetWords = targetWords,
                IsActive = 1,
                CreatedAt = ticks,
                ModifiedAt = ticks,
            };

            tx.Execute(
                "INSERT INTO DailyGoal (Id, ManuscriptId, TargetWords, IsActive, CreatedAt, ModifiedAt) VALUES (?, ?, ?, 1, ?, ?)",
                row.Id, row.ManuscriptId, row.TargetWords, row.CreatedAt, row.ModifiedAt);

            result = row.ToEntity();
        }).ConfigureAwait(false);

        return result!;
    }

    /// <inheritdoc />
    public async Task<bool> ClearAsync(
        Guid manuscriptId,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Deactivated, not deleted. The row keeps the target the writer last chose so that turning
        // tracking back on does not make them retype it, and the writing history — which snapshots
        // its own targets per day — is untouched (FR-010).
        int affected = await connection.ExecuteAsync(
            "UPDATE DailyGoal SET IsActive = 0, ModifiedAt = ? WHERE ManuscriptId = ?",
            RowConversions.ToTicks(timestamp), manuscriptId.ToString()).ConfigureAwait(false);

        return affected > 0;
    }
}
