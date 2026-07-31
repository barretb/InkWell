using InkWell.Application.Abstractions;
using InkWell.Domain.Entities;
using SQLite;

namespace InkWell.Infrastructure.Persistence;

/// <summary>
/// SQLCipher-backed storage for the per-day writing history (FR-011, FR-012).
/// </summary>
/// <remarks>
/// Autosave writes days through <see cref="ChapterRepository.CommitAutoSaveAsync"/> so that prose
/// and its day's total land in one transaction. This repository is the read path plus the
/// out-of-band write path — the same upsert logic, shared with that transaction rather than
/// duplicated.
/// </remarks>
public sealed class WritingHistoryRepository : IWritingHistoryRepository
{
    private readonly ISqliteConnectionFactory _factory;

    /// <summary>Creates the repository.</summary>
    public WritingHistoryRepository(ISqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public async Task<DailyWritingRecord?> GetAsync(
        Guid manuscriptId,
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<DailyWritingRecordRow> rows = await connection.QueryAsync<DailyWritingRecordRow>(
            "SELECT * FROM DailyWritingRecord WHERE ManuscriptId = ? AND Date = ?",
            manuscriptId.ToString(),
            RowConversions.ToText(localDate)).ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0].ToEntity();
    }

    /// <inheritdoc />
    public async Task<DailyWritingRecord> AddWordsAsync(
        Guid manuscriptId,
        DateOnly localDate,
        int deltaWords,
        int? goalTarget,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        await connection.RunInTransactionAsync(tx =>
            ChapterRepository.UpsertDay(tx, manuscriptId.ToString(), localDate, deltaWords, goalTarget))
            .ConfigureAwait(false);

        return (await GetAsync(manuscriptId, localDate, cancellationToken).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailyWritingRecord>> ListAsync(
        Guid manuscriptId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Dates are stored as ISO yyyy-MM-dd, which orders chronologically as text — so the range
        // filter and the newest-first ordering are both plain string comparisons on an index.
        List<DailyWritingRecordRow> rows = await connection.QueryAsync<DailyWritingRecordRow>(
            "SELECT * FROM DailyWritingRecord WHERE ManuscriptId = ? AND Date >= ? AND Date <= ? ORDER BY Date DESC",
            manuscriptId.ToString(),
            RowConversions.ToText(fromDate),
            RowConversions.ToText(toDate)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToEntity())];
    }
}
