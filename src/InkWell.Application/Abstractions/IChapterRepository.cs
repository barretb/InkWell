using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Domain.Services;

namespace InkWell.Application.Abstractions;

/// <summary>
/// Storage for chapters and their prose (contracts/manuscript-service.md,
/// contracts/chapter-editor-bridge.md).
/// </summary>
public interface IChapterRepository
{
    /// <summary>Lists a manuscript's chapters in order, without loading prose.</summary>
    Task<IReadOnlyList<ChapterSummary>> ListSummariesAsync(
        Guid manuscriptId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one chapter's row, or null when it does not exist.</summary>
    Task<Chapter?> GetAsync(Guid chapterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a chapter's markdown together with its inline images resolved to data URIs, ready for
    /// the editor to open.
    /// </summary>
    Task<ChapterContent?> GetContentAsync(Guid chapterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends an empty chapter after the manuscript's last chapter and bumps the manuscript's
    /// modified stamp.
    /// </summary>
    Task AddAsync(Chapter chapter, CancellationToken cancellationToken = default);

    /// <summary>Renames a chapter.</summary>
    /// <returns>False when no chapter has that identifier.</returns>
    Task<bool> RenameAsync(
        Guid chapterId,
        string title,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a computed set of order assignments in one transaction, so the manuscript is never
    /// observable in a half-reordered state (FR-002, US1 scenario 3).
    /// </summary>
    Task ApplyOrderAsync(
        Guid manuscriptId,
        IReadOnlyList<ChapterOrderAssignment> assignments,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a chapter, cascade-deletes its inline images, and re-packs the remaining chapters'
    /// order indices — all in one transaction.
    /// </summary>
    /// <returns>False when no chapter has that identifier.</returns>
    Task<bool> DeleteAsync(Guid chapterId, DateTimeOffset modifiedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits one autosave: chapter prose and word count, the manuscript's modified stamp, and the
    /// day's writing record, in a single transaction (FR-004, SC-003).
    /// </summary>
    /// <returns>The refreshed counts, or null when the chapter no longer exists.</returns>
    Task<AutoSaveResult?> CommitAutoSaveAsync(AutoSaveCommit commit, CancellationToken cancellationToken = default);

    /// <summary>Sums the prose word counts of a manuscript's chapters (FR-009).</summary>
    Task<int> GetManuscriptWordCountAsync(Guid manuscriptId, CancellationToken cancellationToken = default);
}
