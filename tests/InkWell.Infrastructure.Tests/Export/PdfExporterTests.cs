using System.Text;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Application.Abstractions;
using InkWell.Infrastructure.Export;
using InkWell.Infrastructure.Tests.Fixtures;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace InkWell.Infrastructure.Tests.Export;

/// <summary>
/// T109 · FR-018, SC-009 — PDF export at both granularities, with the bundled font resolver in
/// place and every inline image embedded.
/// </summary>
/// <remarks>
/// The font resolver is the highest-risk part of PDF export (research.md §3): PDFsharp's Core build
/// reads no system fonts, so on iOS and Android a document without a resolver renders no text at
/// all. What can be proved here is that the resolver finds its embedded faces and that a rendered
/// document really contains them. Whether those bytes satisfy the device rasterisers is what the
/// T107 device spike is for, and nothing below claims to settle it.
/// </remarks>
public class PdfExporterTests
{
    private static async Task<(StoreFixture Fixture, Guid ManuscriptId)> ManuscriptAsync(
        params (string Title, int Images)[] chapters)
    {
        var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;

        foreach ((string title, int images) in chapters)
        {
            await fixture.WriteChapterWithImagesAsync(manuscript.Id, title, images);
        }

        return (fixture, manuscript.Id);
    }

    // ---- The font resolver (research.md §3) ----

    [Fact]
    public void The_bundled_faces_are_present_in_the_assembly()
    {
        // If the embedded resources ever stop being embedded, PDF export silently produces pages
        // with no text on mobile. This fails the build instead.
        BundledFontResolver resolver = BundledFontResolver.Instance;

        byte[]? regular = resolver.GetFont(resolver.ResolveTypeface(BundledFontResolver.BodyFamily, false, false)!.FaceName);
        byte[]? heading = resolver.GetFont(resolver.ResolveTypeface(BundledFontResolver.HeadingFamily, true, false)!.FaceName);

        Assert.NotNull(regular);
        Assert.NotNull(heading);
        Assert.True(regular!.Length > 10_000);
        Assert.True(heading!.Length > 10_000);

        // A TrueType file starts with one of two known signatures.
        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x00 }, regular.Take(4));
    }

    [Fact]
    public void Every_typeface_request_resolves_rather_than_returning_null()
    {
        // A null from the resolver is not a missing style in PDFsharp — it is an exception in the
        // middle of someone's export.
        BundledFontResolver resolver = BundledFontResolver.Instance;

        foreach (string family in new[] { BundledFontResolver.BodyFamily, BundledFontResolver.HeadingFamily, "Times New Roman", "" })
        {
            foreach (bool bold in new[] { true, false })
            {
                foreach (bool italic in new[] { true, false })
                {
                    Assert.NotNull(resolver.ResolveTypeface(family, bold, italic));
                }
            }
        }
    }

    [Fact]
    public void Bold_and_heading_requests_get_the_heavier_face()
    {
        BundledFontResolver resolver = BundledFontResolver.Instance;

        string regular = resolver.ResolveTypeface(BundledFontResolver.BodyFamily, false, false)!.FaceName;
        string bold = resolver.ResolveTypeface(BundledFontResolver.BodyFamily, true, false)!.FaceName;
        string heading = resolver.ResolveTypeface(BundledFontResolver.HeadingFamily, false, false)!.FaceName;

        Assert.NotEqual(regular, bold);
        Assert.Equal(bold, heading);
    }

    // ---- The document opens, and carries what it should ----

    [Fact]
    public async Task The_exported_file_opens_as_a_pdf()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 0), ("The Mill", 0));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.pdf");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Pdf, path);

            using PdfSharp.Pdf.PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);

            Assert.True(document.PageCount >= 2);
            Assert.Equal("The Long Winter", document.Info.Title);
        }
    }

    [Fact]
    public async Task The_document_embeds_the_bundled_font_rather_than_naming_a_system_one()
    {
        // The whole point of the resolver: the reader's machine must not have to own the typeface.
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 0));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.pdf");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Pdf, path);

            // Read through the object model rather than scanning the file: PDFsharp compresses
            // object streams, so the names are not sitting in the bytes as text.
            using PdfSharp.Pdf.PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);

            PdfDictionary[] descriptors =
            [
                .. document.Internals.GetAllObjects()
                    .OfType<PdfDictionary>()
                    .Where(d => d.Elements.GetName("/Type") == "/FontDescriptor")
            ];

            Assert.NotEmpty(descriptors);

            // FontFile2 is the embedded TrueType program itself. Without the resolver PDFsharp
            // would have had no bytes to embed, and on iOS and Android no system font to fall back
            // to either (research.md §3).
            Assert.All(descriptors, d => Assert.True(
                d.Elements.ContainsKey("/FontFile2"),
                $"The font '{d.Elements.GetName("/FontName")}' is referenced but not embedded."));
        }
    }

    [Fact]
    public async Task Chapter_prose_reaches_the_page()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync();
        await using (fixture)
        {
            Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscriptId, "Snowfall")).Value;
            const string markdown = """
                # The ridge

                Elin watched the **mill** burn from the ridge.

                > She had not meant for it to happen.

                - One
                - Two
                """;

            await fixture.Chapters.CommitAutoSaveAsync(new AutoSaveCommit(
                chapter.Id, markdown, 20, fixture.Clock.Now, fixture.Clock.Today));

            string path = fixture.ExportPath("book.pdf");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Pdf, path);

            using PdfSharp.Pdf.PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);

            // A page count above zero with a real content stream is as far as PDFsharp will let a
            // test see without a text extractor; that the walk did not throw on headings, emphasis,
            // quotes, and lists is the substance of this one.
            Assert.Equal(1, document.PageCount);
            Assert.True(new FileInfo(path).Length > 1_000);
        }
    }

    // ---- Image embedding (SC-009) ----

    [Fact]
    public async Task Every_source_image_is_embedded()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 2), ("The Mill", 1));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.pdf");
            ExportResult result = await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Pdf, path);

            Assert.Equal(3, result.EmbeddedImageCount);

            using PdfSharp.Pdf.PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            string raw = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(path));
            Assert.Contains("/XObject", raw, StringComparison.Ordinal);
            Assert.Contains("/Image", raw, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_chapter_with_no_images_reports_none_embedded()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 0));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.pdf");
            ExportResult result = await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Pdf, path);

            Assert.Equal(0, result.EmbeddedImageCount);
        }
    }

    [Fact]
    public async Task A_reference_whose_bytes_are_gone_prints_a_note_rather_than_failing_the_export()
    {
        // A dangling reference should cost the writer one picture, not their whole export.
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync();
        await using (fixture)
        {
            Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscriptId, "Snowfall")).Value;
            string markdown = $"Before.\n\n![The frozen mill](inkwell-img://{Guid.NewGuid()})\n\nAfter.";
            await fixture.Chapters.CommitAutoSaveAsync(new AutoSaveCommit(
                chapter.Id, markdown, 4, fixture.Clock.Now, fixture.Clock.Today));

            string path = fixture.ExportPath("book.pdf");
            ExportResult result = await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Pdf, path);

            Assert.Equal(0, result.EmbeddedImageCount);
            Assert.True(File.Exists(path));
        }
    }

    // ---- Both granularities (FR-018) ----

    [Fact]
    public async Task A_single_chapter_exports_on_its_own()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 2), ("The Mill", 3));
        await using (fixture)
        {
            IReadOnlyList<ChapterSummary> chapters = await fixture.ChapterUseCases.ListAsync(manuscriptId);

            string path = fixture.ExportPath("chapter.pdf");
            ExportResult result = await fixture.Export.ExportChapterAsync(chapters[0].Id, ExportFormat.Pdf, path);

            using PdfSharp.Pdf.PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);

            Assert.Equal(2, result.EmbeddedImageCount);
            Assert.Equal("Snowfall", document.Info.Title);
        }
    }

    [Fact]
    public async Task Exporting_every_chapter_separately_writes_one_pdf_each()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 1), ("The Mill", 2));
        await using (fixture)
        {
            string folder = Path.Combine(fixture.ExportDirectory, "per-chapter-pdf");

            IReadOnlyList<ExportResult> results = await fixture.Export
                .ExportManuscriptAllChaptersAsync(manuscriptId, ExportFormat.Pdf, folder);

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(File.Exists(r.FilePath)));
            Assert.Equal([1, 2], results.Select(r => r.EmbeddedImageCount));
            Assert.Equal(
                ["01 - Snowfall.pdf", "02 - The Mill.pdf"],
                results.Select(r => Path.GetFileName(r.FilePath)));
        }
    }

    // ---- File naming ----

    [Theory]
    [InlineData("Snowfall", "Snowfall.pdf")]
    [InlineData("Chapter 1: The Mill", "Chapter 1 The Mill.pdf")]
    [InlineData("Who? What/Why", "Who What Why.pdf")]
    [InlineData("   ", "Untitled.pdf")]
    public void Chapter_titles_become_file_names_every_platform_accepts(string title, string expected)
    {
        // Only used for batch export, where the app names the files; everywhere else the writer's
        // chosen path is used exactly as given (FR-018).
        Assert.Equal(expected, ExportFileName.For(title, ExportFormat.Pdf));
    }

    [Fact]
    public void A_very_long_title_is_trimmed_rather_than_producing_an_unopenable_path()
    {
        string name = ExportFileName.For(new string('a', 500), ExportFormat.Epub);

        Assert.True(name.Length <= 85);
        Assert.EndsWith(".epub", name, StringComparison.Ordinal);
    }
}
