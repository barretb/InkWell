using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using SQLite;

namespace InkWell.Infrastructure.Persistence;

/// <summary>
/// SQLCipher-backed storage for embedded image bytes (FR-003a).
/// </summary>
/// <remarks>
/// Bytes are copied in on insert, so the manuscript survives the writer moving or deleting the file
/// they dragged in. They live in their own table, addressed by rowid, so that the chapter row —
/// rewritten every second or two by autosave — stays small.
/// </remarks>
public sealed class InlineImageRepository : IInlineImageRepository
{
    /// <summary>The largest image InkWell will embed, before the writer is told it is too large.</summary>
    public const int MaxImageBytes = 8 * 1024 * 1024;

    private readonly ISqliteConnectionFactory _factory;

    /// <summary>Creates the repository.</summary>
    public InlineImageRepository(ISqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public async Task<InlineImageReference> AddAsync(
        InlineImageInsert insert,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(insert);
        ArgumentNullException.ThrowIfNull(insert.Bytes);

        if (insert.Bytes.Length == 0)
        {
            throw new ArgumentException("An inline image must have content.", nameof(insert));
        }

        if (insert.Bytes.Length > MaxImageBytes)
        {
            throw new ArgumentException(
                $"An inline image may be at most {MaxImageBytes / (1024 * 1024)} MB.", nameof(insert));
        }

        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO InlineImage (Id, ChapterId, Bytes, MimeType, AltText, ByteLength, CreatedAt) VALUES (?, ?, ?, ?, ?, ?, ?)",
            id.ToString(),
            insert.ChapterId.ToString(),
            insert.Bytes,
            insert.MimeType,
            insert.AltText,
            insert.Bytes.Length,
            RowConversions.ToTicks(createdAt)).ConfigureAwait(false);

        return new InlineImageReference(id, insert.MimeType, insert.AltText, ToDataUri(insert.MimeType, insert.Bytes));
    }

    /// <inheritdoc />
    public async Task<InlineImage?> GetAsync(Guid imageId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<InlineImageRow> rows = await connection
            .QueryAsync<InlineImageRow>("SELECT * FROM InlineImage WHERE Id = ?", imageId.ToString())
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0].ToEntity();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InlineImageReference>> ListReferencesAsync(
        Guid chapterId,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<InlineImageRow> rows = await connection
            .QueryAsync<InlineImageRow>(
                "SELECT * FROM InlineImage WHERE ChapterId = ? ORDER BY CreatedAt", chapterId.ToString())
            .ConfigureAwait(false);

        return [.. rows.Select(r => new InlineImageReference(
            Guid.Parse(r.Id), r.MimeType, r.AltText, ToDataUri(r.MimeType, r.Bytes)))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InlineImage>> ListWithBytesAsync(
        Guid chapterId,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<InlineImageRow> rows = await connection
            .QueryAsync<InlineImageRow>(
                "SELECT * FROM InlineImage WHERE ChapterId = ? ORDER BY CreatedAt", chapterId.ToString())
            .ConfigureAwait(false);

        return [.. rows.Select(r => r.ToEntity())];
    }

    /// <inheritdoc />
    public async Task<bool> SetAltTextAsync(Guid imageId, string? altText, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        int affected = await connection.ExecuteAsync(
            "UPDATE InlineImage SET AltText = ? WHERE Id = ?", altText, imageId.ToString()).ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListMissingAltTextAsync(
        Guid manuscriptId,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await _factory.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        List<string> ids = await connection.QueryScalarsAsync<string>(
            """
            SELECT i.Id
            FROM InlineImage i
            JOIN Chapter c ON c.Id = i.ChapterId
            WHERE c.ManuscriptId = ? AND (i.AltText IS NULL OR TRIM(i.AltText) = '')
            """,
            manuscriptId.ToString()).ConfigureAwait(false);

        return [.. ids.Select(Guid.Parse)];
    }

    /// <summary>
    /// Renders bytes as a <c>data:</c> URI so CodeMirror can paint the image inline without a
    /// second bridge round trip (research.md §1).
    /// </summary>
    public static string ToDataUri(string mimeType, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }
}
