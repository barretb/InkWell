using InkWell.Application.Abstractions;
using InkWell.Domain.Entities;

namespace InkWell.Application.Tests.Fakes;

/// <summary>
/// An in-memory store for characters and plot threads.
/// </summary>
/// <remarks>
/// Both types carry the same shape — an identifier, a manuscript, a name, freeform notes, and two
/// timestamps — so they are held in one store with the sorting rule each one's list method
/// promises: characters by name, plot threads by title.
/// </remarks>
public sealed class FakeReferenceRepository : IReferenceRepository
{
    private readonly Dictionary<Guid, Character> _characters = [];
    private readonly Dictionary<Guid, PlotThread> _plotThreads = [];

    /// <summary>The modified stamp the most recent update passed in.</summary>
    public DateTimeOffset? LastModifiedAt { get; private set; }

    /// <summary>Reads a stored character without going through the interface.</summary>
    public Character? PeekCharacter(Guid characterId)
        => _characters.TryGetValue(characterId, out Character? c) ? Copy(c) : null;

    /// <summary>Reads a stored plot thread without going through the interface.</summary>
    public PlotThread? PeekPlotThread(Guid plotThreadId)
        => _plotThreads.TryGetValue(plotThreadId, out PlotThread? t) ? Copy(t) : null;

    /// <inheritdoc />
    public Task<IReadOnlyList<Character>> ListCharactersAsync(
        Guid manuscriptId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Character> characters =
        [
            .. _characters.Values
                .Where(c => c.ManuscriptId == manuscriptId)
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(Copy)
        ];

        return Task.FromResult(characters);
    }

    /// <inheritdoc />
    public Task<Character?> GetCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
        => Task.FromResult(PeekCharacter(characterId));

    /// <inheritdoc />
    public Task AddCharacterAsync(Character character, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        _characters[character.Id] = Copy(character);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> UpdateCharacterAsync(
        Guid characterId,
        string name,
        string notes,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        LastModifiedAt = modifiedAt;
        if (!_characters.TryGetValue(characterId, out Character? character))
        {
            return Task.FromResult(false);
        }

        character.Name = name;
        character.Notes = notes;
        character.ModifiedAt = modifiedAt;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeleteCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
        => Task.FromResult(_characters.Remove(characterId));

    /// <inheritdoc />
    public Task<IReadOnlyList<PlotThread>> ListPlotThreadsAsync(
        Guid manuscriptId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PlotThread> threads =
        [
            .. _plotThreads.Values
                .Where(t => t.ManuscriptId == manuscriptId)
                .OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(Copy)
        ];

        return Task.FromResult(threads);
    }

    /// <inheritdoc />
    public Task<PlotThread?> GetPlotThreadAsync(Guid plotThreadId, CancellationToken cancellationToken = default)
        => Task.FromResult(PeekPlotThread(plotThreadId));

    /// <inheritdoc />
    public Task AddPlotThreadAsync(PlotThread plotThread, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plotThread);
        _plotThreads[plotThread.Id] = Copy(plotThread);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> UpdatePlotThreadAsync(
        Guid plotThreadId,
        string title,
        string notes,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        LastModifiedAt = modifiedAt;
        if (!_plotThreads.TryGetValue(plotThreadId, out PlotThread? thread))
        {
            return Task.FromResult(false);
        }

        thread.Title = title;
        thread.Notes = notes;
        thread.ModifiedAt = modifiedAt;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeletePlotThreadAsync(Guid plotThreadId, CancellationToken cancellationToken = default)
        => Task.FromResult(_plotThreads.Remove(plotThreadId));

    private static Character Copy(Character source) => new()
    {
        Id = source.Id,
        ManuscriptId = source.ManuscriptId,
        Name = source.Name,
        Notes = source.Notes,
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
    };

    private static PlotThread Copy(PlotThread source) => new()
    {
        Id = source.Id,
        ManuscriptId = source.ManuscriptId,
        Title = source.Title,
        Notes = source.Notes,
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
    };
}
