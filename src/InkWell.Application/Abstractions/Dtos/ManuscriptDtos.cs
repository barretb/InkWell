namespace InkWell.Application.Abstractions.Dtos;

/// <summary>
/// A manuscript as the library lists it. Deliberately excludes chapter content so opening the
/// library never loads prose (contracts/manuscript-service.md).
/// </summary>
/// <param name="Id">The manuscript's identifier.</param>
/// <param name="Title">Its title.</param>
/// <param name="ModifiedAt">When it last changed; the library sorts newest-modified first.</param>
/// <param name="ChapterCount">How many chapters it holds.</param>
/// <param name="WordCount">Total prose words across its chapters.</param>
public sealed record ManuscriptSummary(
    Guid Id,
    string Title,
    DateTimeOffset ModifiedAt,
    int ChapterCount,
    int WordCount);

/// <summary>
/// A chapter as the manuscript's chapter list shows it — no prose, so reordering a 50-chapter
/// manuscript never loads 150,000 words (SC-004).
/// </summary>
/// <param name="Id">The chapter's identifier.</param>
/// <param name="Title">Its title.</param>
/// <param name="OrderIndex">Its zero-based position in the manuscript.</param>
/// <param name="WordCount">Its cached prose word count.</param>
public sealed record ChapterSummary(Guid Id, string Title, int OrderIndex, int WordCount);

/// <summary>
/// A manuscript with its ordered chapter summaries, as shown when the writer opens it.
/// </summary>
/// <param name="Id">The manuscript's identifier.</param>
/// <param name="Title">Its title.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="ModifiedAt">When it last changed.</param>
/// <param name="Chapters">Its chapters, in order.</param>
public sealed record ManuscriptDetail(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    IReadOnlyList<ChapterSummary> Chapters)
{
    /// <summary>Total prose words across every chapter (FR-009).</summary>
    public int WordCount => Chapters.Sum(chapter => chapter.WordCount);
}
