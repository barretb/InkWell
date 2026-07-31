using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;
using InkWell.Domain.Services;

namespace InkWell.Application.UseCases;

/// <summary>
/// Adding, renaming, reordering, deleting, and opening chapters (FR-002, FR-003).
/// </summary>
public sealed class ChapterUseCases
{
    private readonly IChapterRepository _chapters;
    private readonly IManuscriptRepository _manuscripts;
    private readonly IClock _clock;

    /// <summary>Creates the use cases.</summary>
    public ChapterUseCases(IChapterRepository chapters, IManuscriptRepository manuscripts, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentNullException.ThrowIfNull(manuscripts);
        ArgumentNullException.ThrowIfNull(clock);
        _chapters = chapters;
        _manuscripts = manuscripts;
        _clock = clock;
    }

    /// <summary>Lists a manuscript's chapters in order.</summary>
    public Task<IReadOnlyList<ChapterSummary>> ListAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
        => _chapters.ListSummariesAsync(manuscriptId, cancellationToken);

    /// <summary>Appends an empty chapter to the end of the manuscript.</summary>
    public async Task<DomainResult<Chapter>> AddAsync(Guid manuscriptId, string? title, CancellationToken cancellationToken = default)
    {
        DomainResult<EntityTitle> validated = EntityTitle.Create(title);
        if (validated.IsFailure)
        {
            return DomainResult<Chapter>.Failure(validated.Error);
        }

        Manuscript? manuscript = await _manuscripts.GetAsync(manuscriptId, cancellationToken).ConfigureAwait(false);
        if (manuscript is null)
        {
            return DomainResult<Chapter>.NotFound("That manuscript no longer exists.");
        }

        IReadOnlyList<ChapterSummary> existing = await _chapters
            .ListSummariesAsync(manuscriptId, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.Now;
        var chapter = new Chapter
        {
            Id = Guid.NewGuid(),
            ManuscriptId = manuscriptId,
            Title = validated.Value.Value,
            ContentMarkdown = string.Empty,
            OrderIndex = ChapterOrdering.NextOrderIndex(existing.Select(c => c.OrderIndex)),
            WordCount = 0,
            CreatedAt = now,
            ModifiedAt = now,
        };

        await _chapters.AddAsync(chapter, cancellationToken).ConfigureAwait(false);
        await _manuscripts.TouchAsync(manuscriptId, now, cancellationToken).ConfigureAwait(false);
        return DomainResult<Chapter>.Success(chapter);
    }

    /// <summary>Renames a chapter.</summary>
    public async Task<DomainResult> RenameAsync(Guid chapterId, string? title, CancellationToken cancellationToken = default)
    {
        DomainResult<EntityTitle> validated = EntityTitle.Create(title);
        if (validated.IsFailure)
        {
            return DomainResult.Failure(validated.Error);
        }

        bool renamed = await _chapters
            .RenameAsync(chapterId, validated.Value.Value, _clock.Now, cancellationToken)
            .ConfigureAwait(false);

        return renamed ? DomainResult.Success() : DomainResult.NotFound("That chapter no longer exists.");
    }

    /// <summary>
    /// Puts the manuscript's chapters into the given order. The new order is persisted, so it
    /// survives closing and reopening the app (US1 scenario 3).
    /// </summary>
    public async Task<DomainResult> ReorderAsync(
        Guid manuscriptId,
        IReadOnlyList<Guid> orderedChapterIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedChapterIds);

        IReadOnlyList<ChapterSummary> existing = await _chapters
            .ListSummariesAsync(manuscriptId, cancellationToken)
            .ConfigureAwait(false);

        DomainResult<IReadOnlyList<ChapterOrderAssignment>> assignments =
            ChapterOrdering.Reorder(existing.Select(c => c.Id), orderedChapterIds);

        if (assignments.IsFailure)
        {
            return DomainResult.Failure(assignments.Error);
        }

        await _chapters
            .ApplyOrderAsync(manuscriptId, assignments.Value, _clock.Now, cancellationToken)
            .ConfigureAwait(false);

        return DomainResult.Success();
    }

    /// <summary>
    /// Moves one chapter by a single position. Exists so the chapter list can be reordered from the
    /// keyboard, which drag-and-drop alone cannot satisfy (FR-019, SC-007).
    /// </summary>
    public async Task<DomainResult> MoveAsync(
        Guid manuscriptId,
        Guid chapterId,
        int offset,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChapterSummary> existing = await _chapters
            .ListSummariesAsync(manuscriptId, cancellationToken)
            .ConfigureAwait(false);

        var order = existing.Select(c => c.Id).ToList();
        int from = order.IndexOf(chapterId);
        if (from < 0)
        {
            return DomainResult.NotFound("That chapter no longer exists.");
        }

        int to = from + offset;
        if (to < 0 || to >= order.Count)
        {
            // Already at the end it is being moved toward: a no-op, not a failure.
            return DomainResult.Success();
        }

        order.RemoveAt(from);
        order.Insert(to, chapterId);

        return await ReorderAsync(manuscriptId, order, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a chapter, its images, and closes the gap in the ordering. The caller is responsible
    /// for having obtained confirmation first (FR-005).
    /// </summary>
    public async Task<DomainResult> DeleteAsync(Guid chapterId, CancellationToken cancellationToken = default)
    {
        bool deleted = await _chapters.DeleteAsync(chapterId, _clock.Now, cancellationToken).ConfigureAwait(false);
        return deleted ? DomainResult.Success() : DomainResult.NotFound("That chapter no longer exists.");
    }

    /// <summary>Loads a chapter's markdown and images for the editor.</summary>
    public async Task<DomainResult<ChapterContent>> GetContentAsync(Guid chapterId, CancellationToken cancellationToken = default)
    {
        ChapterContent? content = await _chapters.GetContentAsync(chapterId, cancellationToken).ConfigureAwait(false);
        return content is null
            ? DomainResult<ChapterContent>.NotFound("That chapter no longer exists.")
            : DomainResult<ChapterContent>.Success(content);
    }

    /// <summary>The manuscript's total prose word count (FR-009).</summary>
    public Task<int> GetManuscriptWordCountAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
        => _chapters.GetManuscriptWordCountAsync(manuscriptId, cancellationToken);
}
