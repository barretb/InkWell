using InkWell.Application.Abstractions;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;

namespace InkWell.Application.UseCases;

/// <summary>
/// Character profiles and plot threads kept alongside a manuscript (FR-013, FR-014, FR-015).
/// </summary>
public sealed class ReferenceUseCases
{
    private readonly IReferenceRepository _references;
    private readonly IManuscriptRepository _manuscripts;
    private readonly IClock _clock;

    /// <summary>Creates the use cases.</summary>
    public ReferenceUseCases(
        IReferenceRepository references,
        IManuscriptRepository manuscripts,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(manuscripts);
        ArgumentNullException.ThrowIfNull(clock);
        _references = references;
        _manuscripts = manuscripts;
        _clock = clock;
    }

    /// <summary>Lists a manuscript's characters, name-sorted.</summary>
    public Task<IReadOnlyList<Character>> ListCharactersAsync(
        Guid manuscriptId,
        CancellationToken cancellationToken = default)
        => _references.ListCharactersAsync(manuscriptId, cancellationToken);

    /// <summary>Creates a character (US4 scenario 1).</summary>
    public async Task<DomainResult<Character>> CreateCharacterAsync(
        Guid manuscriptId,
        string? name,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        DomainResult<EntityTitle> validated = EntityTitle.Create(name);
        if (validated.IsFailure)
        {
            return DomainResult<Character>.Failure(validated.Error);
        }

        Manuscript? manuscript = await _manuscripts.GetAsync(manuscriptId, cancellationToken).ConfigureAwait(false);
        if (manuscript is null)
        {
            return DomainResult<Character>.NotFound("That manuscript no longer exists.");
        }

        DateTimeOffset now = _clock.Now;
        var character = new Character
        {
            Id = Guid.NewGuid(),
            ManuscriptId = manuscriptId,
            Name = validated.Value.Value,
            Notes = notes ?? string.Empty,
            CreatedAt = now,
            ModifiedAt = now,
        };

        await _references.AddCharacterAsync(character, cancellationToken).ConfigureAwait(false);
        return DomainResult<Character>.Success(character);
    }

    /// <summary>Updates a character's name and notes.</summary>
    public async Task<DomainResult> UpdateCharacterAsync(
        Guid characterId,
        string? name,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        DomainResult<EntityTitle> validated = EntityTitle.Create(name);
        if (validated.IsFailure)
        {
            return DomainResult.Failure(validated.Error);
        }

        bool updated = await _references
            .UpdateCharacterAsync(characterId, validated.Value.Value, notes ?? string.Empty, _clock.Now, cancellationToken)
            .ConfigureAwait(false);

        return updated ? DomainResult.Success() : DomainResult.NotFound("That character no longer exists.");
    }

    /// <summary>
    /// Deletes a character. The caller confirms first (FR-005); the manuscript is untouched even if
    /// the writer named this character in their prose.
    /// </summary>
    public async Task<DomainResult> DeleteCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        bool deleted = await _references.DeleteCharacterAsync(characterId, cancellationToken).ConfigureAwait(false);
        return deleted ? DomainResult.Success() : DomainResult.NotFound("That character no longer exists.");
    }

    /// <summary>Lists a manuscript's plot threads, title-sorted.</summary>
    public Task<IReadOnlyList<PlotThread>> ListPlotThreadsAsync(
        Guid manuscriptId,
        CancellationToken cancellationToken = default)
        => _references.ListPlotThreadsAsync(manuscriptId, cancellationToken);

    /// <summary>Creates a plot thread (US4 scenario 2).</summary>
    public async Task<DomainResult<PlotThread>> CreatePlotThreadAsync(
        Guid manuscriptId,
        string? title,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        DomainResult<EntityTitle> validated = EntityTitle.Create(title);
        if (validated.IsFailure)
        {
            return DomainResult<PlotThread>.Failure(validated.Error);
        }

        Manuscript? manuscript = await _manuscripts.GetAsync(manuscriptId, cancellationToken).ConfigureAwait(false);
        if (manuscript is null)
        {
            return DomainResult<PlotThread>.NotFound("That manuscript no longer exists.");
        }

        DateTimeOffset now = _clock.Now;
        var thread = new PlotThread
        {
            Id = Guid.NewGuid(),
            ManuscriptId = manuscriptId,
            Title = validated.Value.Value,
            Notes = notes ?? string.Empty,
            CreatedAt = now,
            ModifiedAt = now,
        };

        await _references.AddPlotThreadAsync(thread, cancellationToken).ConfigureAwait(false);
        return DomainResult<PlotThread>.Success(thread);
    }

    /// <summary>Updates a plot thread's title and notes.</summary>
    public async Task<DomainResult> UpdatePlotThreadAsync(
        Guid plotThreadId,
        string? title,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        DomainResult<EntityTitle> validated = EntityTitle.Create(title);
        if (validated.IsFailure)
        {
            return DomainResult.Failure(validated.Error);
        }

        bool updated = await _references
            .UpdatePlotThreadAsync(plotThreadId, validated.Value.Value, notes ?? string.Empty, _clock.Now, cancellationToken)
            .ConfigureAwait(false);

        return updated ? DomainResult.Success() : DomainResult.NotFound("That plot thread no longer exists.");
    }

    /// <summary>Deletes a plot thread. The caller confirms first (FR-005).</summary>
    public async Task<DomainResult> DeletePlotThreadAsync(Guid plotThreadId, CancellationToken cancellationToken = default)
    {
        bool deleted = await _references.DeletePlotThreadAsync(plotThreadId, cancellationToken).ConfigureAwait(false);
        return deleted ? DomainResult.Success() : DomainResult.NotFound("That plot thread no longer exists.");
    }
}
