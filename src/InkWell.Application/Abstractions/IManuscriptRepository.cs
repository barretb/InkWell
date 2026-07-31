using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;

namespace InkWell.Application.Abstractions;

/// <summary>
/// Storage for manuscripts (contracts/manuscript-service.md). Every method is transactional: a
/// failure rolls back with no partial state, and every method works fully offline (FR-006).
/// </summary>
public interface IManuscriptRepository
{
    /// <summary>
    /// Lists every manuscript for the library, newest-modified first. An empty list is a normal
    /// result, not an error — the caller shows empty-state guidance.
    /// </summary>
    Task<IReadOnlyList<ManuscriptSummary>> ListSummariesAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads one manuscript, or null when it does not exist.</summary>
    Task<Manuscript?> GetAsync(Guid manuscriptId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a manuscript with its ordered chapter summaries — titles, positions, and word counts,
    /// but never chapter prose or image bytes.
    /// </summary>
    Task<ManuscriptDetail?> GetDetailAsync(Guid manuscriptId, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new manuscript.</summary>
    Task AddAsync(Manuscript manuscript, CancellationToken cancellationToken = default);

    /// <summary>Renames a manuscript and bumps its modified stamp.</summary>
    /// <returns>False when no manuscript has that identifier.</returns>
    Task<bool> RenameAsync(
        Guid manuscriptId,
        string title,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a manuscript and, in the same transaction, every chapter, inline image, character,
    /// plot thread, goal, and writing record it owns (FR-018, SC-008).
    /// </summary>
    /// <returns>False when no manuscript has that identifier.</returns>
    Task<bool> DeleteAsync(Guid manuscriptId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that something inside the manuscript changed, so the library's ordering stays
    /// truthful after a chapter edit.
    /// </summary>
    Task TouchAsync(Guid manuscriptId, DateTimeOffset modifiedAt, CancellationToken cancellationToken = default);
}
