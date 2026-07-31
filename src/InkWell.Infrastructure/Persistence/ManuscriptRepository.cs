using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using SQLite;

namespace InkWell.Infrastructure.Persistence;

/// <summary>
/// SQLCipher-backed manuscript storage (contracts/manuscript-service.md).
/// </summary>
public sealed class ManuscriptRepository : IManuscriptRepository
{
    private readonly ISqliteConnectionFactory _factory;

    /// <summary>Creates the repository.</summary>
    public ManuscriptRepository(ISqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManuscriptSummary>> ListSummariesAsync(CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        // One query rather than N+1: the library shows counts for every manuscript, and a writer
        // with fifty manuscripts should not cost fifty round trips.
        List<SummaryRow> rows = await connection.QueryAsync<SummaryRow>(
            """
            SELECT  m.Id            AS Id,
                    m.Title         AS Title,
                    m.ModifiedAt    AS ModifiedAt,
                    COUNT(c.Id)     AS ChapterCount,
                    COALESCE(SUM(c.WordCount), 0) AS WordCount
            FROM Manuscript m
            LEFT JOIN Chapter c ON c.ManuscriptId = m.Id
            GROUP BY m.Id, m.Title, m.ModifiedAt
            ORDER BY m.ModifiedAt DESC
            """).ConfigureAwait(false);

        return [.. rows.Select(r => new ManuscriptSummary(
            Guid.Parse(r.Id),
            r.Title,
            RowConversions.FromTicks(r.ModifiedAt),
            r.ChapterCount,
            r.WordCount))];
    }

    /// <inheritdoc />
    public async Task<Manuscript?> GetAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<ManuscriptRow> rows = await connection
            .QueryAsync<ManuscriptRow>("SELECT * FROM Manuscript WHERE Id = ?", manuscriptId.ToString())
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0].ToEntity();
    }

    /// <inheritdoc />
    public async Task<ManuscriptDetail?> GetDetailAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        Manuscript? manuscript = await GetAsync(manuscriptId, cancellationToken).ConfigureAwait(false);
        if (manuscript is null)
        {
            return null;
        }

        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Chapter prose is deliberately excluded: opening a 150,000-word manuscript must not read
        // 150,000 words (SC-004).
        List<ChapterSummaryRow> chapters = await connection.QueryAsync<ChapterSummaryRow>(
            "SELECT Id, Title, OrderIndex, WordCount FROM Chapter WHERE ManuscriptId = ? ORDER BY OrderIndex",
            manuscriptId.ToString()).ConfigureAwait(false);

        return new ManuscriptDetail(
            manuscript.Id,
            manuscript.Title,
            manuscript.CreatedAt,
            manuscript.ModifiedAt,
            [.. chapters.Select(c => new ChapterSummary(Guid.Parse(c.Id), c.Title, c.OrderIndex, c.WordCount))]);
    }

    /// <inheritdoc />
    public async Task AddAsync(Manuscript manuscript, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manuscript);
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.InsertAsync(ManuscriptRow.FromEntity(manuscript)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RenameAsync(
        Guid manuscriptId,
        string title,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        int affected = await connection.ExecuteAsync(
            "UPDATE Manuscript SET Title = ?, ModifiedAt = ? WHERE Id = ?",
            title, RowConversions.ToTicks(modifiedAt), manuscriptId.ToString()).ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Chapters, images, characters, plot threads, the goal, and the writing history all go with
        // it through ON DELETE CASCADE, inside SQLite's own statement transaction (FR-018, SC-008).
        int affected = await connection
            .ExecuteAsync("DELETE FROM Manuscript WHERE Id = ?", manuscriptId.ToString())
            .ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task TouchAsync(Guid manuscriptId, DateTimeOffset modifiedAt, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "UPDATE Manuscript SET ModifiedAt = ? WHERE Id = ?",
            RowConversions.ToTicks(modifiedAt), manuscriptId.ToString()).ConfigureAwait(false);
    }

    private sealed class SummaryRow
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public long ModifiedAt { get; set; }

        public int ChapterCount { get; set; }

        public int WordCount { get; set; }
    }

    internal sealed class ChapterSummaryRow
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public int OrderIndex { get; set; }

        public int WordCount { get; set; }
    }
}
