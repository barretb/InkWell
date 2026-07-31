namespace InkWell.Application.Abstractions.Dtos;

/// <summary>
/// An inline image resolved for the editor: the bytes are handed over as a data URI so CodeMirror
/// can render the image without a second round trip (research.md §1).
/// </summary>
/// <param name="Id">The image's identifier, as referenced by <c>inkwell-img://{Id}</c>.</param>
/// <param name="MimeType">Its MIME type.</param>
/// <param name="AltText">Its alternative text, if the writer supplied any.</param>
/// <param name="DataUri">A <c>data:</c> URI carrying the image bytes.</param>
public sealed record InlineImageReference(Guid Id, string MimeType, string? AltText, string DataUri)
{
    /// <summary>Whether this image still needs alternative text (FR-019).</summary>
    public bool IsMissingAltText => string.IsNullOrWhiteSpace(AltText);
}

/// <summary>
/// Everything the editor needs to open one chapter (contracts/chapter-editor-bridge.md).
/// </summary>
/// <param name="Id">The chapter's identifier.</param>
/// <param name="ManuscriptId">The manuscript it belongs to.</param>
/// <param name="Title">Its title.</param>
/// <param name="ContentMarkdown">Its markdown source — the editor's document buffer.</param>
/// <param name="Images">Its inline images, resolved to data URIs.</param>
public sealed record ChapterContent(
    Guid Id,
    Guid ManuscriptId,
    string Title,
    string ContentMarkdown,
    IReadOnlyList<InlineImageReference> Images);

/// <summary>
/// A request to embed an image in a chapter (FR-003a).
/// </summary>
/// <param name="ChapterId">The chapter receiving the image.</param>
/// <param name="Bytes">The image bytes, copied into the encrypted store.</param>
/// <param name="MimeType">The image's MIME type.</param>
/// <param name="AltText">Alternative text, if the writer supplied any.</param>
public sealed record InlineImageInsert(Guid ChapterId, byte[] Bytes, string MimeType, string? AltText);

/// <summary>
/// One autosave commit. Chapter prose, the manuscript's modified stamp, and the day's writing
/// record are written in a single transaction so counts can never diverge from content
/// (data-model.md §Persistence &amp; integrity notes).
/// </summary>
/// <param name="ChapterId">The chapter being saved.</param>
/// <param name="ContentMarkdown">Its markdown, exactly as the editor holds it.</param>
/// <param name="WordCount">
/// The prose word count, always recomputed by the host from <paramref name="ContentMarkdown"/> and
/// never taken from the editor (FR-009).
/// </param>
/// <param name="Timestamp">The moment of the commit.</param>
/// <param name="LocalDate">
/// The local calendar day the words belong to, so post-midnight typing lands on the new day
/// (FR-012).
/// </param>
public sealed record AutoSaveCommit(
    Guid ChapterId,
    string ContentMarkdown,
    int WordCount,
    DateTimeOffset Timestamp,
    DateOnly LocalDate);

/// <summary>
/// The counts an autosave produced, returned so the editor can update its live display without a
/// second query.
/// </summary>
/// <param name="ChapterWordCount">The saved chapter's prose word count.</param>
/// <param name="ManuscriptWordCount">The whole manuscript's prose word count.</param>
/// <param name="WordsWrittenToday">Net prose words attributed to the current local day.</param>
/// <param name="DailyGoalTarget">
/// The active daily target at the moment of the commit, or null when no goal is set. Returned
/// alongside the counts because the transaction already reads it to snapshot the day's record —
/// asking again from the view model would be a second query on the keystroke path.
/// </param>
public sealed record AutoSaveResult(
    int ChapterWordCount,
    int ManuscriptWordCount,
    int WordsWrittenToday,
    int? DailyGoalTarget);
