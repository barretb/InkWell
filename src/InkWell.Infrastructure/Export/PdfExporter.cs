using InkWell.Application.Abstractions;
using InkWell.Domain.Entities;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;

namespace InkWell.Infrastructure.Export;

/// <summary>
/// Renders a manuscript to PDF by walking the Markdig syntax tree into MigraDoc elements
/// (research.md §3).
/// </summary>
/// <remarks>
/// <para>
/// PDFsharp with MigraDoc is the only free PDF stack that runs pure-managed on all four MAUI
/// targets — QuestPDF dropped mobile entirely, and iText's licence does not suit a distributed
/// closed-source app. MigraDoc's flowing-document model is also the right shape for a novel: pages
/// break themselves, and the exporter never has to reason about coordinates.
/// </para>
/// <para>
/// The walk goes over the AST rather than over rendered HTML, because the tree is where an image
/// node can be recognised and swapped for its embedded bytes. Anything the walk does not have a
/// specific mapping for still renders as its own literal text, so an unusual construct degrades to
/// readable prose rather than vanishing from the writer's book.
/// </para>
/// </remarks>
public sealed class PdfExporter
{
    private readonly IMarkdownService _markdown;

    /// <summary>Creates the exporter.</summary>
    /// <param name="markdown">Parses chapter markdown to its syntax tree.</param>
    public PdfExporter(IMarkdownService markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        _markdown = markdown;
    }

    /// <summary>
    /// Writes <paramref name="source"/> to <paramref name="destinationPath"/> as a PDF.
    /// </summary>
    /// <returns>How many images were embedded (SC-009).</returns>
    public async Task<int> WriteAsync(
        ExportSource source,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        using var buffer = new MemoryStream();
        int imageCount = Build(source, buffer);

        buffer.Position = 0;
        await using FileStream file = File.Create(destinationPath);
        await buffer.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

        return imageCount;
    }

    /// <summary>Renders into <paramref name="output"/>.</summary>
    /// <returns>The number of images embedded.</returns>
    public int Build(ExportSource source, Stream output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        // Without this, PDFsharp's Core build finds no typeface on iOS or Android and the document
        // comes out with no text at all (research.md §3).
        BundledFontResolver.EnsureInstalled();

        var document = new Document { Info = { Title = source.Title } };
        DefineStyles(document);

        int embedded = 0;
        foreach (ExportChapter chapter in source.Chapters)
        {
            Section section = document.AddSection();
            section.PageSetup.PageFormat = PageFormat.A5;
            section.PageSetup.TopMargin = Unit.FromCentimeter(2);
            section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
            section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
            section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);

            Paragraph heading = section.AddParagraph(chapter.Title);
            heading.Style = "ChapterHeading";

            var images = chapter.Images.ToDictionary(image => image.Id);
            var byOrder = new Queue<InlineImage>(chapter.Images);

            MarkdownDocument tree = _markdown.Parse(chapter.Markdown);
            foreach (Block block in tree)
            {
                embedded += RenderBlock(section, block, images, byOrder);
            }
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, closeStream: false);

        return embedded;
    }

    private static void DefineStyles(Document document)
    {
        Style normal = document.Styles["Normal"]!;
        normal.Font.Name = BundledFontResolver.BodyFamily;
        normal.Font.Size = 11;
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
        normal.ParagraphFormat.LineSpacing = 1.35;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);

        Style chapterHeading = document.Styles.AddStyle("ChapterHeading", "Normal");
        chapterHeading.Font.Name = BundledFontResolver.HeadingFamily;
        chapterHeading.Font.Size = 20;
        chapterHeading.Font.Bold = true;
        chapterHeading.ParagraphFormat.SpaceAfter = Unit.FromPoint(18);

        for (int level = 1; level <= 6; level++)
        {
            Style style = document.Styles.AddStyle($"Heading{level}", "Normal");
            style.Font.Name = BundledFontResolver.HeadingFamily;
            style.Font.Size = Math.Max(11, 18 - (level * 2));
            style.Font.Bold = true;
            style.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
            style.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);
            style.ParagraphFormat.KeepWithNext = true;
        }

        Style quote = document.Styles.AddStyle("Quote", "Normal");
        quote.Font.Italic = true;
        quote.ParagraphFormat.LeftIndent = Unit.FromCentimeter(0.8);
    }

    private static int RenderBlock(
        Section section,
        Block block,
        IReadOnlyDictionary<Guid, InlineImage> images,
        Queue<InlineImage> byOrder)
    {
        switch (block)
        {
            case HeadingBlock heading:
            {
                Paragraph paragraph = section.AddParagraph();
                paragraph.Style = $"Heading{Math.Clamp(heading.Level, 1, 6)}";
                return RenderInlines(section, paragraph, heading.Inline, images, byOrder);
            }

            case ParagraphBlock body:
            {
                Paragraph paragraph = section.AddParagraph();
                return RenderInlines(section, paragraph, body.Inline, images, byOrder);
            }

            case QuoteBlock quote:
            {
                int embedded = 0;
                foreach (Block child in quote)
                {
                    embedded += RenderBlock(section, child, images, byOrder);
                    // The style is applied after the fact because the child block decides its own
                    // paragraph; this keeps quoted prose recognisable without a separate walker.
                    if (section.Elements[^1] is Paragraph paragraph)
                    {
                        paragraph.Style = "Quote";
                    }
                }

                return embedded;
            }

            case ListBlock list:
            {
                int embedded = 0;
                int ordinal = 1;
                foreach (Block item in list)
                {
                    if (item is not ListItemBlock listItem)
                    {
                        continue;
                    }

                    foreach (Block child in listItem)
                    {
                        Paragraph paragraph = section.AddParagraph();
                        paragraph.Format.LeftIndent = Unit.FromCentimeter(0.7);
                        paragraph.AddText(list.IsOrdered ? $"{ordinal}.  " : "•  ");

                        if (child is LeafBlock leaf)
                        {
                            embedded += RenderInlines(section, paragraph, leaf.Inline, images, byOrder);
                        }
                    }

                    ordinal++;
                }

                return embedded;
            }

            case CodeBlock code:
            {
                Paragraph paragraph = section.AddParagraph(GetCodeText(code));
                paragraph.Format.LeftIndent = Unit.FromCentimeter(0.7);
                paragraph.Format.Font.Name = BundledFontResolver.BodyFamily;
                paragraph.Format.Font.Size = 9.5;
                return 0;
            }

            case ThematicBreakBlock:
            {
                Paragraph paragraph = section.AddParagraph("* * *");
                paragraph.Format.Alignment = ParagraphAlignment.Center;
                paragraph.Format.SpaceBefore = Unit.FromPoint(10);
                paragraph.Format.SpaceAfter = Unit.FromPoint(10);
                return 0;
            }

            case ContainerBlock container:
            {
                int embedded = 0;
                foreach (Block child in container)
                {
                    embedded += RenderBlock(section, child, images, byOrder);
                }

                return embedded;
            }

            default:
                return 0;
        }
    }

    private static int RenderInlines(
        Section section,
        Paragraph paragraph,
        ContainerInline? inlines,
        IReadOnlyDictionary<Guid, InlineImage> images,
        Queue<InlineImage> byOrder)
    {
        if (inlines is null)
        {
            return 0;
        }

        int embedded = 0;
        foreach (Inline inline in inlines)
        {
            embedded += RenderInline(section, paragraph, inline, images, byOrder);
        }

        return embedded;
    }

    private static int RenderInline(
        Section section,
        Paragraph paragraph,
        Inline inline,
        IReadOnlyDictionary<Guid, InlineImage> images,
        Queue<InlineImage> byOrder)
    {
        switch (inline)
        {
            case LiteralInline literal:
                paragraph.AddText(literal.Content.ToString());
                return 0;

            case LineBreakInline lineBreak:
                if (lineBreak.IsHard)
                {
                    paragraph.AddLineBreak();
                }
                else
                {
                    paragraph.AddSpace(1);
                }

                return 0;

            case CodeInline code:
            {
                FormattedText formatted = paragraph.AddFormattedText(code.Content);
                formatted.Font.Name = BundledFontResolver.BodyFamily;
                return 0;
            }

            case LinkInline { IsImage: true } image:
                return RenderImage(section, image, images, byOrder);

            case LinkInline link:
            {
                // The link text carries the meaning; the URL is appended so a printed page does not
                // lose it entirely.
                int embedded = RenderInlines(section, paragraph, link, images, byOrder);
                if (!string.IsNullOrWhiteSpace(link.Url))
                {
                    paragraph.AddText($" ({link.Url})");
                }

                return embedded;
            }

            case EmphasisInline emphasis:
            {
                var text = new FormattedTextScope(paragraph, emphasis);
                return text.Render(section, images, byOrder);
            }

            case TaskList task:
                paragraph.AddText(task.Checked ? "[x] " : "[ ] ");
                return 0;

            case ContainerInline container:
                return RenderInlines(section, paragraph, container, images, byOrder);

            default:
                // Anything without a mapping still contributes its own text, so nothing the writer
                // typed silently disappears from their book.
                paragraph.AddText(inline.ToString() ?? string.Empty);
                return 0;
        }
    }

    /// <summary>
    /// Places an image from the encrypted store, or falls back to its alt text.
    /// </summary>
    /// <remarks>
    /// Images are matched by <c>inkwell-img://{id}</c> reference first and by insertion order
    /// second, because the editor writes pasted images as <c>data:</c> URIs. MigraDoc reads image
    /// bytes through a base64 pseudo-path, which is what lets the bytes go straight from the
    /// encrypted database into the PDF without ever being written to a temporary file — an export
    /// must not leave plaintext manuscript content lying on disk (FR-016).
    /// </remarks>
    private static int RenderImage(
        Section section,
        LinkInline image,
        IReadOnlyDictionary<Guid, InlineImage> images,
        Queue<InlineImage> byOrder)
    {
        InlineImage? resolved = null;
        string url = image.Url ?? string.Empty;

        const string Scheme = "inkwell-img://";
        if (url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(url[Scheme.Length..].Trim('/'), out Guid id)
            && images.TryGetValue(id, out InlineImage? byId))
        {
            resolved = byId;
        }
        else if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && byOrder.Count > 0)
        {
            resolved = byOrder.Dequeue();
        }

        string? altText = image.FirstChild?.ToString();

        if (resolved is null)
        {
            // The reference points at bytes that are not here. Saying so beats a blank space that
            // looks like the writer left a gap.
            Paragraph missing = section.AddParagraph($"[Image not available{(string.IsNullOrWhiteSpace(altText) ? string.Empty : $": {altText}")}]");
            missing.Format.Font.Italic = true;
            return 0;
        }

        Paragraph holder = section.AddParagraph();
        holder.Format.Alignment = ParagraphAlignment.Center;

        Image placed = holder.AddImage(
            "base64:" + Convert.ToBase64String(resolved.Bytes));
        placed.LockAspectRatio = true;
        placed.Width = Unit.FromCentimeter(10);

        if (!string.IsNullOrWhiteSpace(altText))
        {
            Paragraph caption = section.AddParagraph(altText);
            caption.Format.Alignment = ParagraphAlignment.Center;
            caption.Format.Font.Size = 9;
            caption.Format.Font.Italic = true;
        }

        return 1;
    }

    private static string GetCodeText(CodeBlock code)
        => code.Lines.Lines.Take(code.Lines.Count).Select(line => line.ToString()).Aggregate(
            new System.Text.StringBuilder(),
            (builder, line) => builder.AppendLine(line),
            builder => builder.ToString().TrimEnd());

    /// <summary>
    /// Applies bold or italic to everything an emphasis span contains.
    /// </summary>
    /// <remarks>
    /// MigraDoc styles a <see cref="FormattedText"/> run rather than a range, so emphasis has to
    /// wrap its children instead of toggling a flag and untoggling it afterwards.
    /// </remarks>
    private sealed class FormattedTextScope(Paragraph paragraph, EmphasisInline emphasis)
    {
        public int Render(
            Section section,
            IReadOnlyDictionary<Guid, InlineImage> images,
            Queue<InlineImage> byOrder)
        {
            FormattedText run = paragraph.AddFormattedText();
            if (emphasis.DelimiterCount >= 2)
            {
                run.Bold = true;
            }
            else
            {
                run.Italic = true;
            }

            int embedded = 0;
            foreach (Inline child in emphasis)
            {
                if (child is LiteralInline literal)
                {
                    run.AddText(literal.Content.ToString());
                }
                else
                {
                    embedded += RenderInline(section, paragraph, child, images, byOrder);
                }
            }

            return embedded;
        }
    }
}
