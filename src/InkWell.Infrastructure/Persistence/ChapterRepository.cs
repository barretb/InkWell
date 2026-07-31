using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Domain.Services;
using SQLite;

namespace InkWell.Infrastructure.Persistence;

/// <summary>
/// SQLCipher-backed chapter storage, including the transactional autosave commit.
/// </summary>
public sealed class ChapterRepository : IChapterRepository
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly IInlineImageRepository _images;

    /// <summary>Creates the repository.</summary>
    public ChapterRepository(ISqliteConnectionFactory factory, IInlineImageRepository images)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(images);
        _factory = factory;
        _images = images;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChapterSummary>> ListSummariesAsync(
        Guid manuscriptId,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<ManuscriptRepository.ChapterSummaryRow> rows = await connection
            .QueryAsync<ManuscriptRepository.ChapterSummaryRow>(
                "SELECT Id, Title, OrderIndex, WordCount FROM Chapter WHERE ManuscriptId = ? ORDER BY OrderIndex",
                manuscriptId.ToString())
            .ConfigureAwait(false);

        return [.. rows.Select(r => new ChapterSummary(Guid.Parse(r.Id), r.Title, r.OrderIndex, r.WordCount))];
    }

    /// <inheritdoc />
    public async Task<Chapter?> GetAsync(Guid chapterId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<ChapterRow> rows = await connection
            .QueryAsync<ChapterRow>("SELECT * FROM Chapter WHERE Id = ?", chapterId.ToString())
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0].ToEntity();
    }

    /// <inheritdoc />
    public async Task<ChapterContent?> GetContentAsync(Guid chapterId, CancellationToken cancellationToken = default)
    {
        Chapter? chapter = await GetAsync(chapterId, cancellationToken).ConfigureAwait(false);
        if (chapter is null)
        {
            return null;
        }

        IReadOnlyList<InlineImageReference> images = await _images
            .ListReferencesAsync(chapterId, cancellationToken)
            .ConfigureAwait(false);

        return new ChapterContent(
            chapter.Id,
            chapter.ManuscriptId,
            chapter.Title,
            chapter.ContentMarkdown,
            images);
    }

    /// <inheritdoc />
    public async Task AddAsync(Chapter chapter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.InsertAsync(ChapterRow.FromEntity(chapter)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RenameAsync(
        Guid chapterId,
        string title,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        long ticks = RowConversions.ToTicks(modifiedAt);

        var affected = 0;
        await connection.RunInTransactionAsync(tx =>
        {
            affected = tx.Execute(
                "UPDATE Chapter SET Title = ?, ModifiedAt = ? WHERE Id = ?",
                title, ticks, chapterId.ToString());

            if (affected > 0)
            {
                tx.Execute(
                    "UPDATE Manuscript SET ModifiedAt = ? WHERE Id = (SELECT ManuscriptId FROM Chapter WHERE Id = ?)",
                    ticks, chapterId.ToString());
            }
        }).ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task ApplyOrderAsync(
        Guid manuscriptId,
        IReadOnlyList<ChapterOrderAssignment> assignments,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        long ticks = RowConversions.ToTicks(modifiedAt);

        // One transaction, so the manuscript is never observable with two chapters claiming the
        // same position (US1 scenario 3).
        await connection.RunInTransactionAsync(tx =>
        {
            foreach (ChapterOrderAssignment assignment in assignments)
            {
                tx.Execute(
                    "UPDATE Chapter SET OrderIndex = ? WHERE Id = ? AND ManuscriptId = ?",
                    assignment.OrderIndex, assignment.ChapterId.ToString(), manuscriptId.ToString());
            }

            tx.Execute("UPDATE Manuscript SET ModifiedAt = ? WHERE Id = ?", ticks, manuscriptId.ToString());
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid chapterId,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        long ticks = RowConversions.ToTicks(modifiedAt);
        var deleted = false;

        await connection.RunInTransactionAsync(tx =>
        {
            List<ChapterRow> owner = tx.Query<ChapterRow>(
                "SELECT * FROM Chapter WHERE Id = ?", chapterId.ToString());

            if (owner.Count == 0)
            {
                return;
            }

            string manuscriptId = owner[0].ManuscriptId;

            // The chapter's images go with it through ON DELETE CASCADE.
            tx.Execute("DELETE FROM Chapter WHERE Id = ?", chapterId.ToString());

            // Close the gap so positions stay contiguous, using the same rule the domain applies.
            List<string> remaining = tx.QueryScalars<string>(
                "SELECT Id FROM Chapter WHERE ManuscriptId = ? ORDER BY OrderIndex", manuscriptId);

            foreach (ChapterOrderAssignment assignment in ChapterOrdering.Repack(remaining.Select(Guid.Parse)))
            {
                tx.Execute(
                    "UPDATE Chapter SET OrderIndex = ? WHERE Id = ?",
                    assignment.OrderIndex, assignment.ChapterId.ToString());
            }

            tx.Execute("UPDATE Manuscript SET ModifiedAt = ? WHERE Id = ?", ticks, manuscriptId);
            deleted = true;
        }).ConfigureAwait(false);

        return deleted;
    }

    /// <inheritdoc />
    public async Task<AutoSaveResult?> CommitAutoSaveAsync(
        AutoSaveCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        AutoSaveResult? result = null;

        // Prose, its word count, the manuscript's modified stamp, and the day's writing record are
        // all written together. A partially applied autosave would leave the counts the writer sees
        // disagreeing with the words on the page, and the daily goal is the count they trust most
        // (data-model.md §Persistence & integrity notes, FR-012).
        await connection.RunInTransactionAsync(tx =>
        {
            List<ChapterRow> existing = tx.Query<ChapterRow>(
                "SELECT * FROM Chapter WHERE Id = ?", commit.ChapterId.ToString());

            if (existing.Count == 0)
            {
                return;
            }

            ChapterRow row = existing[0];
            long timestamp = RowConversions.ToTicks(commit.Timestamp);

            // The day's total moves by the *change* in this chapter's count, so re-saving an old
            // chapter unchanged credits nothing and deleting prose subtracts.
            int delta = DailyProgressCalculator.WordsDelta(row.WordCount, commit.WordCount);

            tx.Execute(
                "UPDATE Chapter SET ContentMarkdown = ?, WordCount = ?, ModifiedAt = ? WHERE Id = ?",
                commit.ContentMarkdown,
                commit.WordCount,
                timestamp,
                commit.ChapterId.ToString());

            tx.Execute("UPDATE Manuscript SET ModifiedAt = ? WHERE Id = ?", timestamp, row.ManuscriptId);

            int manuscriptWords = tx.ExecuteScalar<int>(
                "SELECT COALESCE(SUM(WordCount), 0) FROM Chapter WHERE ManuscriptId = ?", row.ManuscriptId);

            int? activeTarget = ReadActiveTarget(tx, row.ManuscriptId);
            int wordsToday = UpsertDay(tx, row.ManuscriptId, commit.LocalDate, delta, activeTarget);

            result = new AutoSaveResult(commit.WordCount, manuscriptWords, wordsToday, activeTarget);
        }).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Reads the manuscript's active daily target inside an open transaction, or null when no goal
    /// is set or the writer cleared it.
    /// </summary>
    internal static int? ReadActiveTarget(SQLiteConnection tx, string manuscriptId)
    {
        List<DailyGoalRow> goals = tx.Query<DailyGoalRow>(
            "SELECT * FROM DailyGoal WHERE ManuscriptId = ? AND IsActive = 1", manuscriptId);

        return goals.Count > 0 && goals[0].TargetWords > 0 ? goals[0].TargetWords : null;
    }

    /// <summary>
    /// Adds <paramref name="delta"/> words to one local day, creating the row on the day's first
    /// save, and re-snapshots the target and met flag.
    /// </summary>
    /// <remarks>
    /// The target is snapshotted per day rather than joined at read time so that changing or
    /// clearing the goal tomorrow cannot rewrite what yesterday's achievement was (FR-012).
    /// </remarks>
    /// <returns>The day's total after the update.</returns>
    internal static int UpsertDay(
        SQLiteConnection tx,
        string manuscriptId,
        DateOnly localDate,
        int delta,
        int? activeTarget)
    {
        string date = RowConversions.ToText(localDate);

        List<DailyWritingRecordRow> existing = tx.Query<DailyWritingRecordRow>(
            "SELECT * FROM DailyWritingRecord WHERE ManuscriptId = ? AND Date = ?", manuscriptId, date);

        int previous = existing.Count > 0 ? existing[0].WordsWritten : 0;
        int total = DailyProgressCalculator.ApplyDelta(previous, delta);
        bool met = activeTarget is { } target && total >= target;

        if (existing.Count > 0)
        {
            tx.Execute(
                "UPDATE DailyWritingRecord SET WordsWritten = ?, GoalTarget = ?, GoalMet = ? WHERE Id = ?",
                total, activeTarget, met ? 1 : 0, existing[0].Id);
        }
        else
        {
            tx.Execute(
                "INSERT INTO DailyWritingRecord (Id, ManuscriptId, Date, WordsWritten, GoalTarget, GoalMet) " +
                "VALUES (?, ?, ?, ?, ?, ?)",
                Guid.NewGuid().ToString(), manuscriptId, date, total, activeTarget, met ? 1 : 0);
        }

        return total;
    }

    /// <inheritdoc />
    public async Task<int> GetManuscriptWordCountAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COALESCE(SUM(WordCount), 0) FROM Chapter WHERE ManuscriptId = ?",
            manuscriptId.ToString()).ConfigureAwait(false);
    }
}
