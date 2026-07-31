using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;

namespace InkWell.Application.UseCases;

/// <summary>
/// Creating, renaming, opening, and deleting manuscripts (FR-001, contracts/manuscript-service.md).
/// </summary>
public sealed class ManuscriptUseCases
{
    private readonly IManuscriptRepository _manuscripts;
    private readonly IClock _clock;

    /// <summary>Creates the use cases.</summary>
    public ManuscriptUseCases(IManuscriptRepository manuscripts, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(manuscripts);
        ArgumentNullException.ThrowIfNull(clock);
        _manuscripts = manuscripts;
        _clock = clock;
    }

    /// <summary>Lists every manuscript for the library, newest-modified first.</summary>
    public Task<IReadOnlyList<ManuscriptSummary>> ListAsync(CancellationToken cancellationToken = default)
        => _manuscripts.ListSummariesAsync(cancellationToken);

    /// <summary>Creates a manuscript so it appears in the library immediately (US1 scenario 1).</summary>
    public async Task<DomainResult<Manuscript>> CreateAsync(string? title, CancellationToken cancellationToken = default)
    {
        DomainResult<EntityTitle> validated = EntityTitle.Create(title);
        if (validated.IsFailure)
        {
            return DomainResult<Manuscript>.Failure(validated.Error);
        }

        DateTimeOffset now = _clock.Now;
        var manuscript = new Manuscript
        {
            Id = Guid.NewGuid(),
            Title = validated.Value.Value,
            CreatedAt = now,
            ModifiedAt = now,
        };

        await _manuscripts.AddAsync(manuscript, cancellationToken).ConfigureAwait(false);
        return DomainResult<Manuscript>.Success(manuscript);
    }

    /// <summary>Renames a manuscript and bumps its modified stamp.</summary>
    public async Task<DomainResult> RenameAsync(Guid manuscriptId, string? title, CancellationToken cancellationToken = default)
    {
        DomainResult<EntityTitle> validated = EntityTitle.Create(title);
        if (validated.IsFailure)
        {
            return DomainResult.Failure(validated.Error);
        }

        bool renamed = await _manuscripts
            .RenameAsync(manuscriptId, validated.Value.Value, _clock.Now, cancellationToken)
            .ConfigureAwait(false);

        return renamed ? DomainResult.Success() : DomainResult.NotFound("That manuscript no longer exists.");
    }

    /// <summary>
    /// Deletes a manuscript and everything it owns. The caller is responsible for having obtained
    /// confirmation first (FR-005) — this layer performs the deletion it is told to perform.
    /// </summary>
    public async Task<DomainResult> DeleteAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        bool deleted = await _manuscripts.DeleteAsync(manuscriptId, cancellationToken).ConfigureAwait(false);
        return deleted ? DomainResult.Success() : DomainResult.NotFound("That manuscript no longer exists.");
    }

    /// <summary>Opens a manuscript, returning it with its ordered chapter summaries.</summary>
    public async Task<DomainResult<ManuscriptDetail>> GetAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        ManuscriptDetail? detail = await _manuscripts.GetDetailAsync(manuscriptId, cancellationToken).ConfigureAwait(false);
        return detail is null
            ? DomainResult<ManuscriptDetail>.NotFound("That manuscript no longer exists.")
            : DomainResult<ManuscriptDetail>.Success(detail);
    }
}
