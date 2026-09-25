using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Domain.Services;

namespace InkWell.Application.Tests.Fakes;

/// <summary>
/// An in-memory chapter store that honours ordering but not transactions.
/// </summary>
/// <remarks>
/// Transactional guarantees are a property of the real SQLite repository and are asserted there.
/// What this fake exists to expose is the order assignment a use case computed and the timestamp it
/// chose — the two things <see cref="ChapterUseCases"/> is actually responsible for.
/// </remarks>
public sealed class FakeChapterRepository : IChapterRepository
{
    private readonly Dictionary<Guid, Chapter> _chapters = [];

    /// <summary>The assignments the last <see cref="ApplyOrderAsync"/> received.</summary>
    public IReadOnlyList<ChapterOrderAssignment> LastOrderApplied { get; private set; } = [];

    /// <summary>How many times <see cref="ApplyOrderAsync"/> was called.</summary>
    public int ApplyOrderCount { get; private set; }

    /// <summary>The modified stamp the most recent write passed in.</summary>
    public DateTimeOffset? LastModifiedAt { get; private set; }

    /// <summary>How many chapters a manuscript holds.</summary>
    public int CountFor(Guid manuscriptId) => _chapters.Values.Count(c => c.ManuscriptId == manuscriptId);

    /// <summary>A manuscript's total prose word count.</summary>
    public int WordCountFor(Guid manuscriptId)
        => _chapters.Values.Where(c => c.ManuscriptId == manuscriptId).Sum(c => c.WordCount);

    /// <summary>A manuscript's chapter summaries, in order.</summary>
    public IReadOnlyList<ChapterSummary> SummariesFor(Guid manuscriptId) =>
    [
        .. _chapters.Values
            .Where(c => c.ManuscriptId == manuscriptId)
            .OrderBy(c => c.OrderIndex)
            .Select(c => new ChapterSummary(c.Id, c.Title, c.OrderIndex, c.WordCount))
    ];

    /// <summary>Reads a stored chapter without going through the interface.</summary>
    public Chapter? Peek(Guid chapterId)
        => _chapters.TryGetValue(chapterId, out Chapter? c) ? Copy(c) : null;

    /// <summary>Drops every chapter belonging to a manuscript, as a cascade delete would.</summary>
    public void RemoveManuscript(Guid manuscriptId)
    {
        foreach (Guid id in _chapters.Values.Where(c => c.ManuscriptId == manuscriptId).Select(c => c.Id).ToList())
        {
            _chapters.Remove(id);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChapterSummary>> ListSummariesAsync(
        Guid manuscriptId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(SummariesFor(manuscriptId));

    /// <inheritdoc />
    public Task<Chapter?> GetAsync(Guid chapterId, CancellationToken cancellationToken = default)
        => Task.FromResult(Peek(chapterId));

    /// <inheritdoc />
    public Task<ChapterContent?> GetContentAsync(Guid chapterId, CancellationToken cancellationToken = default)
    {
        if (!_chapters.TryGetValue(chapterId, out Chapter? chapter))
        {
            return Task.FromResult<ChapterContent?>(null);
        }

        return Task.FromResult<ChapterContent?>(new ChapterContent(
            chapter.Id,
            chapter.ManuscriptId,
            chapter.Title,
            chapter.ContentMarkdown,
            []));
    }

    /// <inheritdoc />
    public Task AddAsync(Chapter chapter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        _chapters[chapter.Id] = Copy(chapter);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RenameAsync(
        Guid chapterId,
        string title,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        LastModifiedAt = modifiedAt;
        if (!_chapters.TryGetValue(chapterId, out Chapter? chapter))
        {
            return Task.FromResult(false);
        }

        chapter.Title = title;
        chapter.ModifiedAt = modifiedAt;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task ApplyOrderAsync(
        Guid manuscriptId,
        IReadOnlyList<ChapterOrderAssignment> assignments,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ApplyOrderCount++;
        LastOrderApplied = assignments;
        LastModifiedAt = modifiedAt;

        foreach (ChapterOrderAssignment assignment in assignments)
        {
            if (_chapters.TryGetValue(assignment.ChapterId, out Chapter? chapter))
            {
                chapter.OrderIndex = assignment.OrderIndex;
                chapter.ModifiedAt = modifiedAt;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid chapterId, DateTimeOffset modifiedAt, CancellationToken cancellationToken = default)
    {
        LastModifiedAt = modifiedAt;
        if (!_chapters.TryGetValue(chapterId, out Chapter? chapter))
        {
            return Task.FromResult(false);
        }

        Guid manuscriptId = chapter.ManuscriptId;
        _chapters.Remove(chapterId);

        // Re-pack, exactly as the real repository does, so a test can assert the gap closed.
        int index = 0;
        foreach (Chapter remaining in _chapters.Values
            .Where(c => c.ManuscriptId == manuscriptId)
            .OrderBy(c => c.OrderIndex))
        {
            remaining.OrderIndex = index++;
        }

        return Task.FromResult(true);
    }

    /// <summary>Optional controlled delay/failure before a commit, for lifecycle tests.</summary>
    public Func<AutoSaveCommit, Task>? BeforeCommit { get; set; }

    /// <inheritdoc />
    public async Task<AutoSaveResult?> CommitAutoSaveAsync(AutoSaveCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (BeforeCommit is not null)
        {
            await BeforeCommit(commit);
        }
        if (!_chapters.TryGetValue(commit.ChapterId, out Chapter? chapter))
        {
            return null;
        }

        chapter.ContentMarkdown = commit.ContentMarkdown;
        chapter.WordCount = commit.WordCount;
        chapter.ModifiedAt = commit.Timestamp;
        LastModifiedAt = commit.Timestamp;

        int manuscriptWords = WordCountFor(chapter.ManuscriptId);
        return new AutoSaveResult(commit.WordCount, manuscriptWords, manuscriptWords, null);
    }

    /// <inheritdoc />
    public Task<int> GetManuscriptWordCountAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
        => Task.FromResult(WordCountFor(manuscriptId));

    private static Chapter Copy(Chapter source) => new()
    {
        Id = source.Id,
        ManuscriptId = source.ManuscriptId,
        Title = source.Title,
        ContentMarkdown = source.ContentMarkdown,
        OrderIndex = source.OrderIndex,
        WordCount = source.WordCount,
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
    };
}
