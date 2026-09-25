using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;

namespace InkWell.Application.Tests.Fakes;

/// <summary>
/// An in-memory manuscript store.
/// </summary>
/// <remarks>
/// <para>
/// The integration suite already proves the real repository against a keyed database. What this
/// fake buys is the ability to observe what a use case <em>asked the store to do</em> — which
/// timestamp it passed, whether it touched the manuscript at all — without inferring it from a
/// round trip. That is the difference between testing orchestration and testing SQL.
/// </para>
/// <para>
/// Entities are copied in and out. They are mutable classes, so handing back the stored instance
/// would let a test mutate the store by accident and would hide a use case that forgot to write.
/// </para>
/// </remarks>
public sealed class FakeManuscriptRepository : IManuscriptRepository
{
    private readonly Dictionary<Guid, Manuscript> _manuscripts = [];

    /// <summary>The chapter fake, when one is wired up, so summaries can report chapter counts.</summary>
    public FakeChapterRepository? Chapters { get; set; }

    /// <summary>How many manuscripts are stored.</summary>
    public int Count => _manuscripts.Count;

    /// <summary>How many times <see cref="TouchAsync"/> was called.</summary>
    public int TouchCount { get; private set; }

    /// <summary>The modified stamp the most recent write passed in.</summary>
    public DateTimeOffset? LastModifiedAt { get; private set; }

    /// <summary>Seeds a manuscript directly, bypassing the use case.</summary>
    public Manuscript Seed(string title = "Seeded", DateTimeOffset? createdAt = null)
    {
        DateTimeOffset at = createdAt ?? DateTimeOffset.UnixEpoch;
        var manuscript = new Manuscript
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAt = at,
            ModifiedAt = at,
        };

        _manuscripts[manuscript.Id] = manuscript;
        return Copy(manuscript);
    }

    /// <summary>Reads a stored manuscript without going through the interface.</summary>
    public Manuscript? Peek(Guid manuscriptId)
        => _manuscripts.TryGetValue(manuscriptId, out Manuscript? m) ? Copy(m) : null;

    /// <inheritdoc />
    public Task<IReadOnlyList<ManuscriptSummary>> ListSummariesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ManuscriptSummary> summaries =
        [
            .. _manuscripts.Values
                .OrderByDescending(m => m.ModifiedAt)
                .Select(m => new ManuscriptSummary(
                    m.Id,
                    m.Title,
                    m.ModifiedAt,
                    Chapters?.CountFor(m.Id) ?? 0,
                    Chapters?.WordCountFor(m.Id) ?? 0))
        ];

        return Task.FromResult(summaries);
    }

    /// <inheritdoc />
    public Task<Manuscript?> GetAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
        => Task.FromResult(Peek(manuscriptId));

    /// <inheritdoc />
    public Task<ManuscriptDetail?> GetDetailAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        if (!_manuscripts.TryGetValue(manuscriptId, out Manuscript? manuscript))
        {
            return Task.FromResult<ManuscriptDetail?>(null);
        }

        return Task.FromResult<ManuscriptDetail?>(new ManuscriptDetail(
            manuscript.Id,
            manuscript.Title,
            manuscript.CreatedAt,
            manuscript.ModifiedAt,
            Chapters?.SummariesFor(manuscriptId) ?? []));
    }

    /// <inheritdoc />
    public Task AddAsync(Manuscript manuscript, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manuscript);
        _manuscripts[manuscript.Id] = Copy(manuscript);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RenameAsync(
        Guid manuscriptId,
        string title,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        LastModifiedAt = modifiedAt;
        if (!_manuscripts.TryGetValue(manuscriptId, out Manuscript? manuscript))
        {
            return Task.FromResult(false);
        }

        manuscript.Title = title;
        manuscript.ModifiedAt = modifiedAt;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        bool removed = _manuscripts.Remove(manuscriptId);
        if (removed)
        {
            Chapters?.RemoveManuscript(manuscriptId);
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task TouchAsync(Guid manuscriptId, DateTimeOffset modifiedAt, CancellationToken cancellationToken = default)
    {
        TouchCount++;
        LastModifiedAt = modifiedAt;
        if (_manuscripts.TryGetValue(manuscriptId, out Manuscript? manuscript))
        {
            manuscript.ModifiedAt = modifiedAt;
        }

        return Task.CompletedTask;
    }

    private static Manuscript Copy(Manuscript source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
    };
}
