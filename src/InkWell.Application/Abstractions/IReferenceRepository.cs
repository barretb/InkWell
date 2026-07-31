using InkWell.Domain.Entities;

namespace InkWell.Application.Abstractions;

/// <summary>
/// Storage for characters and plot threads (contracts/reference-service.md). These are reference
/// material: deleting one removes that entry only and can never corrupt manuscript prose, even if
/// the writer mentioned it in a chapter.
/// </summary>
public interface IReferenceRepository
{
    /// <summary>Lists a manuscript's characters, name-sorted.</summary>
    Task<IReadOnlyList<Character>> ListCharactersAsync(Guid manuscriptId, CancellationToken cancellationToken = default);

    /// <summary>Loads one character, or null when it does not exist.</summary>
    Task<Character?> GetCharacterAsync(Guid characterId, CancellationToken cancellationToken = default);

    /// <summary>Inserts a character.</summary>
    Task AddCharacterAsync(Character character, CancellationToken cancellationToken = default);

    /// <summary>Updates a character's name and notes.</summary>
    /// <returns>False when no character has that identifier.</returns>
    Task<bool> UpdateCharacterAsync(
        Guid characterId,
        string name,
        string notes,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a character.</summary>
    /// <returns>False when no character has that identifier.</returns>
    Task<bool> DeleteCharacterAsync(Guid characterId, CancellationToken cancellationToken = default);

    /// <summary>Lists a manuscript's plot threads, title-sorted.</summary>
    Task<IReadOnlyList<PlotThread>> ListPlotThreadsAsync(Guid manuscriptId, CancellationToken cancellationToken = default);

    /// <summary>Loads one plot thread, or null when it does not exist.</summary>
    Task<PlotThread?> GetPlotThreadAsync(Guid plotThreadId, CancellationToken cancellationToken = default);

    /// <summary>Inserts a plot thread.</summary>
    Task AddPlotThreadAsync(PlotThread plotThread, CancellationToken cancellationToken = default);

    /// <summary>Updates a plot thread's title and notes.</summary>
    /// <returns>False when no plot thread has that identifier.</returns>
    Task<bool> UpdatePlotThreadAsync(
        Guid plotThreadId,
        string title,
        string notes,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a plot thread.</summary>
    /// <returns>False when no plot thread has that identifier.</returns>
    Task<bool> DeletePlotThreadAsync(Guid plotThreadId, CancellationToken cancellationToken = default);
}
