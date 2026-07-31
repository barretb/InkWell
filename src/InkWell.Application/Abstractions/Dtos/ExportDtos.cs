namespace InkWell.Application.Abstractions.Dtos;

/// <summary>The file formats a manuscript or chapter can be exported to (FR-018).</summary>
public enum ExportFormat
{
    /// <summary>EPUB 3, assembled as a zip archive with images as real entries.</summary>
    Epub = 0,

    /// <summary>PDF, rendered through MigraDoc.</summary>
    Pdf = 1,
}

/// <summary>
/// The outcome of one export (SC-009).
/// </summary>
/// <param name="FilePath">Where the file was written — always a location the writer chose.</param>
/// <param name="Format">The format written.</param>
/// <param name="ByteLength">The file's size.</param>
/// <param name="EmbeddedImageCount">
/// How many inline images were embedded. Reported so a test — and the writer — can confirm that
/// every source image made it into the output.
/// </param>
public sealed record ExportResult(string FilePath, ExportFormat Format, long ByteLength, int EmbeddedImageCount);

/// <summary>
/// A count of everything InkWell has stored for one manuscript, so the writer can see exactly what
/// the app holds (FR-018).
/// </summary>
/// <param name="ManuscriptId">The manuscript's identifier.</param>
/// <param name="Title">Its title.</param>
/// <param name="ChapterCount">How many chapters it holds.</param>
/// <param name="WordCount">Total prose words.</param>
/// <param name="InlineImageCount">How many embedded images.</param>
/// <param name="InlineImageBytes">Total bytes those images occupy.</param>
/// <param name="CharacterCount">How many character profiles.</param>
/// <param name="PlotThreadCount">How many plot threads.</param>
/// <param name="HasDailyGoal">Whether a daily goal is stored.</param>
/// <param name="WritingRecordCount">How many days of writing history.</param>
public sealed record ManuscriptDataInventory(
    Guid ManuscriptId,
    string Title,
    int ChapterCount,
    int WordCount,
    int InlineImageCount,
    long InlineImageBytes,
    int CharacterCount,
    int PlotThreadCount,
    bool HasDailyGoal,
    int WritingRecordCount);

/// <summary>
/// Everything the app has stored, across all manuscripts (FR-018 "view all of their data").
/// </summary>
/// <param name="Manuscripts">One entry per manuscript.</param>
/// <param name="DatabasePath">Where the encrypted database file lives on this device.</param>
/// <param name="DatabaseByteLength">How large that file currently is.</param>
public sealed record DataInventory(
    IReadOnlyList<ManuscriptDataInventory> Manuscripts,
    string DatabasePath,
    long DatabaseByteLength);
