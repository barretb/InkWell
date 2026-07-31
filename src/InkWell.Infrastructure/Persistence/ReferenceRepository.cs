using InkWell.Application.Abstractions;
using InkWell.Domain.Entities;
using SQLite;

namespace InkWell.Infrastructure.Persistence;

/// <summary>
/// SQLCipher-backed storage for characters and plot threads (contracts/reference-service.md).
/// </summary>
/// <remarks>
/// These are reference material kept alongside the manuscript, never inside it. Nothing here writes
/// to <c>Chapter</c>, which is what makes the spec's edge case — deleting a character the writer
/// mentioned in their prose — structurally incapable of corrupting the manuscript rather than
/// merely tested not to.
/// </remarks>
public sealed class ReferenceRepository : IReferenceRepository
{
    private readonly ISqliteConnectionFactory _factory;

    /// <summary>Creates the repository.</summary>
    public ReferenceRepository(ISqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Character>> ListCharactersAsync(
        Guid manuscriptId,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<CharacterRow> rows = await connection.QueryAsync<CharacterRow>(
            "SELECT * FROM Character WHERE ManuscriptId = ? ORDER BY Name COLLATE NOCASE",
            manuscriptId.ToString()).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToEntity())];
    }

    /// <inheritdoc />
    public async Task<Character?> GetCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<CharacterRow> rows = await connection
            .QueryAsync<CharacterRow>("SELECT * FROM Character WHERE Id = ?", characterId.ToString())
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0].ToEntity();
    }

    /// <inheritdoc />
    public async Task AddCharacterAsync(Character character, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            "INSERT INTO Character (Id, ManuscriptId, Name, Notes, CreatedAt, ModifiedAt) VALUES (?, ?, ?, ?, ?, ?)",
            character.Id.ToString(),
            character.ManuscriptId.ToString(),
            character.Name,
            character.Notes,
            RowConversions.ToTicks(character.CreatedAt),
            RowConversions.ToTicks(character.ModifiedAt)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateCharacterAsync(
        Guid characterId,
        string name,
        string notes,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        int affected = await connection.ExecuteAsync(
            "UPDATE Character SET Name = ?, Notes = ?, ModifiedAt = ? WHERE Id = ?",
            name, notes, RowConversions.ToTicks(modifiedAt), characterId.ToString()).ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        int affected = await connection
            .ExecuteAsync("DELETE FROM Character WHERE Id = ?", characterId.ToString())
            .ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlotThread>> ListPlotThreadsAsync(
        Guid manuscriptId,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<PlotThreadRow> rows = await connection.QueryAsync<PlotThreadRow>(
            "SELECT * FROM PlotThread WHERE ManuscriptId = ? ORDER BY Title COLLATE NOCASE",
            manuscriptId.ToString()).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToEntity())];
    }

    /// <inheritdoc />
    public async Task<PlotThread?> GetPlotThreadAsync(Guid plotThreadId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<PlotThreadRow> rows = await connection
            .QueryAsync<PlotThreadRow>("SELECT * FROM PlotThread WHERE Id = ?", plotThreadId.ToString())
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0].ToEntity();
    }

    /// <inheritdoc />
    public async Task AddPlotThreadAsync(PlotThread plotThread, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plotThread);
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            "INSERT INTO PlotThread (Id, ManuscriptId, Title, Notes, CreatedAt, ModifiedAt) VALUES (?, ?, ?, ?, ?, ?)",
            plotThread.Id.ToString(),
            plotThread.ManuscriptId.ToString(),
            plotThread.Title,
            plotThread.Notes,
            RowConversions.ToTicks(plotThread.CreatedAt),
            RowConversions.ToTicks(plotThread.ModifiedAt)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> UpdatePlotThreadAsync(
        Guid plotThreadId,
        string title,
        string notes,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        int affected = await connection.ExecuteAsync(
            "UPDATE PlotThread SET Title = ?, Notes = ?, ModifiedAt = ? WHERE Id = ?",
            title, notes, RowConversions.ToTicks(modifiedAt), plotThreadId.ToString()).ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> DeletePlotThreadAsync(Guid plotThreadId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        int affected = await connection
            .ExecuteAsync("DELETE FROM PlotThread WHERE Id = ?", plotThreadId.ToString())
            .ConfigureAwait(false);

        return affected > 0;
    }
}
