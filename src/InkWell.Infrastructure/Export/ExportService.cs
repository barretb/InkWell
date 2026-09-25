using System.Globalization;
using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;

namespace InkWell.Infrastructure.Export;

/// <summary>
/// The only path by which manuscript content leaves the device (contracts/export-service.md,
/// FR-017, FR-018).
/// </summary>
/// <remarks>
/// <para>
/// Whole-manuscript and single-chapter export are the same generator over a different chapter list.
/// That is deliberate: SC-009 asks for every inline image to be embedded at both granularities, and
/// two code paths would be two chances to embed them differently.
/// </para>
/// <para>
/// Every write goes to a path the caller supplied, and the caller obtained it from the platform's
/// own save dialog. Nothing here chooses a destination, and nothing here opens a network
/// connection — the app does not even hold the permission to (FR-017, asserted in
/// <c>PlatformPrivacyManifestTests</c>).
/// </para>
/// </remarks>
public sealed class ExportService : IExportService
{
    private readonly IManuscriptRepository _manuscripts;
    private readonly IChapterRepository _chapters;
    private readonly IInlineImageRepository _images;
    private readonly EpubExporter _epub;
    private readonly PdfExporter _pdf;

    /// <summary>Creates the export service.</summary>
    public ExportService(
        IManuscriptRepository manuscripts,
        IChapterRepository chapters,
        IInlineImageRepository images,
        IMarkdownService markdown)
    {
        ArgumentNullException.ThrowIfNull(manuscripts);
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(markdown);

        _manuscripts = manuscripts;
        _chapters = chapters;
        _images = images;
        _epub = new EpubExporter(markdown);
        _pdf = new PdfExporter(markdown);
    }

    /// <inheritdoc />
    public async Task<ExportResult> ExportManuscriptAsync(
        Guid manuscriptId,
        ExportFormat format,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ExportSource source = await GatherManuscriptAsync(manuscriptId, cancellationToken).ConfigureAwait(false);
        return await WriteAsync(source, format, destinationPath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ExportResult> ExportChapterAsync(
        Guid chapterId,
        ExportFormat format,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        Chapter chapter = await _chapters.GetAsync(chapterId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("That chapter no longer exists.");

        Manuscript? manuscript = await _manuscripts.GetAsync(chapter.ManuscriptId, cancellationToken).ConfigureAwait(false);
        ExportChapter prepared = await PrepareAsync(chapter, cancellationToken).ConfigureAwait(false);

        var source = new ExportSource(
            chapter.Title,
            [prepared],
            chapter.Id,
            manuscript?.ModifiedAt ?? chapter.ModifiedAt);

        return await WriteAsync(source, format, destinationPath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExportResult>> ExportManuscriptAllChaptersAsync(
        Guid manuscriptId,
        ExportFormat format,
        string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);

        ExportSource source = await GatherManuscriptAsync(manuscriptId, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(destinationFolder);

        var results = new List<ExportResult>(source.Chapters.Count);
        for (int i = 0; i < source.Chapters.Count; i++)
        {
            ExportChapter chapter = source.Chapters[i];

            // Numbered, so the files sort in reading order in the writer's file manager, and so two
            // chapters that share a title do not overwrite one another.
            string name = string.Create(CultureInfo.InvariantCulture, $"{i + 1:D2} - ")
                + ExportFileName.For(chapter.Title, format);

            var single = new ExportSource(chapter.Title, [chapter], chapter.Id, source.ModifiedAt);
            results.Add(await WriteAsync(
                single,
                format,
                Path.Combine(destinationFolder, name),
                cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<ExportResult> WriteAsync(
        ExportSource source,
        ExportFormat format,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        int embedded = format switch
        {
            ExportFormat.Epub => await _epub.WriteAsync(source, destinationPath, cancellationToken).ConfigureAwait(false),
            ExportFormat.Pdf => await _pdf.WriteAsync(source, destinationPath, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        return new ExportResult(destinationPath, format, new FileInfo(destinationPath).Length, embedded);
    }

    private async Task<ExportSource> GatherManuscriptAsync(Guid manuscriptId, CancellationToken cancellationToken)
    {
        Manuscript manuscript = await _manuscripts.GetAsync(manuscriptId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("That manuscript no longer exists.");

        IReadOnlyList<ChapterSummary> summaries = await _chapters
            .ListSummariesAsync(manuscriptId, cancellationToken)
            .ConfigureAwait(false);

        var chapters = new List<ExportChapter>(summaries.Count);
        foreach (ChapterSummary summary in summaries)
        {
            Chapter? chapter = await _chapters.GetAsync(summary.Id, cancellationToken).ConfigureAwait(false);
            if (chapter is not null)
            {
                chapters.Add(await PrepareAsync(chapter, cancellationToken).ConfigureAwait(false));
            }
        }

        return new ExportSource(manuscript.Title, chapters, manuscript.Id, manuscript.ModifiedAt);
    }

    private async Task<ExportChapter> PrepareAsync(Chapter chapter, CancellationToken cancellationToken)
    {
        // Bytes are loaded only here, at export time. They are deliberately absent from every other
        // read path so autosave and chapter loading never carry them (research.md §2).
        IReadOnlyList<InlineImage> images = await _images
            .ListWithBytesAsync(chapter.Id, cancellationToken)
            .ConfigureAwait(false);

        return new ExportChapter(chapter.Id, chapter.Title, chapter.ContentMarkdown, images);
    }
}
