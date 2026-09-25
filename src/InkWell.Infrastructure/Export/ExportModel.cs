using InkWell.Domain.Entities;

namespace InkWell.Infrastructure.Export;

/// <summary>
/// One chapter, with its markdown and its image bytes, ready to render.
/// </summary>
/// <param name="Id">The chapter's identifier.</param>
/// <param name="Title">Its title, used as the heading and as its table-of-contents entry.</param>
/// <param name="Markdown">Its prose.</param>
/// <param name="Images">Its embedded images, bytes included.</param>
public sealed record ExportChapter(Guid Id, string Title, string Markdown, IReadOnlyList<InlineImage> Images);

/// <summary>
/// Everything an exporter needs, gathered before any file is opened.
/// </summary>
/// <remarks>
/// The two exporters take this rather than repositories, so neither one can decide for itself what
/// "the whole manuscript" or "just this chapter" means. Whole-manuscript and per-chapter export
/// (FR-018) are the same generator over a different chapter list, which is the only reason both
/// granularities can be trusted to embed images identically (SC-009).
/// </remarks>
/// <param name="Title">The book title.</param>
/// <param name="Chapters">The chapters to render, in reading order.</param>
/// <param name="Identifier">A stable identifier for the EPUB package.</param>
/// <param name="ModifiedAt">The source's last-modified stamp, written into EPUB metadata.</param>
public sealed record ExportSource(
    string Title,
    IReadOnlyList<ExportChapter> Chapters,
    Guid Identifier,
    DateTimeOffset ModifiedAt)
{
    /// <summary>Every image across every chapter, in the order they will be written.</summary>
    public IReadOnlyList<InlineImage> AllImages => [.. Chapters.SelectMany(chapter => chapter.Images)];
}
