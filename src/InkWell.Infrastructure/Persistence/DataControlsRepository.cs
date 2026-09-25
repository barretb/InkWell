using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using SQLite;

namespace InkWell.Infrastructure.Persistence;

/// <summary>
/// Lets the writer see and erase everything InkWell holds (FR-018, SC-008).
/// </summary>
/// <remarks>
/// The counterpart to export: one control shows what is stored, another removes it. Together they
/// are what makes "your writing stays on your device" a promise the writer can act on rather than
/// one they have to take on trust.
/// </remarks>
public sealed class DataControlsRepository : IDataControlsRepository
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly IAppStoragePaths _paths;

    /// <summary>Creates the repository.</summary>
    public DataControlsRepository(
        ISqliteConnectionFactory factory,
        IAppStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(paths);
        _factory = factory;
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<DataInventory> GetInventoryAsync(CancellationToken cancellationToken = default)
    {
        using SqliteConnectionLease lease = await _factory.AcquireConnectionAsync(cancellationToken).ConfigureAwait(false);
        SQLiteAsyncConnection connection = lease.Connection;

        // One aggregate query per manuscript rather than per entity type: the inventory screen is
        // read-only and rarely opened, and this keeps it a single pass over indexed columns.
        List<InventoryRow> rows = await connection.QueryAsync<InventoryRow>(
            """
            SELECT
                m.Id                AS ManuscriptId,
                m.Title             AS Title,
                (SELECT COUNT(*) FROM Chapter c WHERE c.ManuscriptId = m.Id)                    AS ChapterCount,
                (SELECT COALESCE(SUM(c.WordCount), 0) FROM Chapter c WHERE c.ManuscriptId = m.Id) AS WordCount,
                (SELECT COUNT(*) FROM InlineImage i
                    JOIN Chapter c ON c.Id = i.ChapterId WHERE c.ManuscriptId = m.Id)           AS InlineImageCount,
                (SELECT COALESCE(SUM(i.ByteLength), 0) FROM InlineImage i
                    JOIN Chapter c ON c.Id = i.ChapterId WHERE c.ManuscriptId = m.Id)           AS InlineImageBytes,
                (SELECT COUNT(*) FROM Character ch WHERE ch.ManuscriptId = m.Id)                AS CharacterCount,
                (SELECT COUNT(*) FROM PlotThread p WHERE p.ManuscriptId = m.Id)                 AS PlotThreadCount,
                (SELECT COUNT(*) FROM DailyGoal g WHERE g.ManuscriptId = m.Id)                  AS GoalCount,
                (SELECT COUNT(*) FROM DailyWritingRecord r WHERE r.ManuscriptId = m.Id)         AS WritingRecordCount
            FROM Manuscript m
            ORDER BY m.ModifiedAt DESC
            """).ConfigureAwait(false);

        IReadOnlyList<ManuscriptDataInventory> manuscripts =
        [
            .. rows.Select(row => new ManuscriptDataInventory(
                Guid.Parse(row.ManuscriptId),
                row.Title,
                row.ChapterCount,
                row.WordCount,
                row.InlineImageCount,
                row.InlineImageBytes,
                row.CharacterCount,
                row.PlotThreadCount,
                row.GoalCount > 0,
                row.WritingRecordCount))
        ];

        string path = _paths.DatabaseFilePath;
        long size = File.Exists(path) ? new FileInfo(path).Length : 0;

        return new DataInventory(manuscripts, path, size);
    }

    /// <inheritdoc />
    public Task<bool> DeleteManuscriptDataAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
        // Foreign keys are ON with cascade delete, so removing the root row removes everything it
        // owns in one transaction (data-model.md §Persistence & integrity notes).
        => new ManuscriptRepository(_factory).DeleteAsync(manuscriptId, cancellationToken);

    /// <summary>Removes the encrypted store and its key, leaving the app ready for new writing.</summary>
    public Task DeleteAllDataAsync(CancellationToken cancellationToken = default)
        => _factory.DeleteDatabaseAsync(cancellationToken);

    private sealed class InventoryRow
    {
        public string ManuscriptId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public int ChapterCount { get; set; }

        public int WordCount { get; set; }

        public int InlineImageCount { get; set; }

        public long InlineImageBytes { get; set; }

        public int CharacterCount { get; set; }

        public int PlotThreadCount { get; set; }

        public int GoalCount { get; set; }

        public int WritingRecordCount { get; set; }
    }
}
