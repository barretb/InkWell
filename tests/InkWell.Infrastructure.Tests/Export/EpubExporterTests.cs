using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Tests.Fixtures;

namespace InkWell.Infrastructure.Tests.Export;

/// <summary>
/// T108 · FR-018, SC-009 — EPUB export at both granularities, with every inline image embedded and
/// a package structure that readers and EPUBCheck will accept.
/// </summary>
public class EpubExporterTests
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

    private static ZipArchive Open(string path) => ZipFile.OpenRead(path);

    private static string TextOf(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"The EPUB has no '{entryName}' entry.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] BytesOf(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // ---- The two classic ways to produce a file that is not really an EPUB ----

    [Fact]
    public async Task The_mimetype_entry_is_first_and_stored_uncompressed()
    {
        // The single most common EPUB packaging error. Readers that check it refuse the book
        // outright, and they do it silently.
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 0));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);

            ZipArchiveEntry first = archive.Entries[0];
            Assert.Equal("mimetype", first.FullName);
            Assert.Equal(first.Length, first.CompressedLength);
            Assert.Equal("application/epub+zip", Encoding.ASCII.GetString(BytesOf(first)));
        }
    }

    [Fact]
    public async Task Every_content_document_is_well_formed_xml()
    {
        // T112: Markdig emits HTML5 — unclosed <img>, <br>, <hr> — and an EPUB content document
        // must parse as XML. This is what the XHTML pass in MarkdownService exists for.
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 2));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            string[] xmlEntries =
            [
                .. archive.Entries
                    .Select(e => e.FullName)
                    .Where(name => name.EndsWith(".xhtml", StringComparison.Ordinal)
                        || name.EndsWith(".opf", StringComparison.Ordinal)
                        || name.EndsWith(".ncx", StringComparison.Ordinal)
                        || name.EndsWith(".xml", StringComparison.Ordinal))
            ];

            Assert.NotEmpty(xmlEntries);
            foreach (string name in xmlEntries)
            {
                // Throws on malformed markup, which is exactly the assertion.
                XDocument.Parse(TextOf(archive, name));
            }
        }
    }

    [Fact]
    public async Task Markdown_that_produces_void_elements_still_parses_as_xml()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync();
        await using (fixture)
        {
            Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscriptId, "Snowfall")).Value;
            const string markdown = "Line one  \nLine two\n\n---\n\nAfter the rule.";
            await fixture.Chapters.CommitAutoSaveAsync(
                new AutoSaveCommit(chapter.Id, markdown, 7, fixture.Clock.Now, fixture.Clock.Today));

            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            string xhtml = TextOf(archive, "OEBPS/chapter0001.xhtml");

            XDocument.Parse(xhtml);
            Assert.Contains("<br />", xhtml, StringComparison.Ordinal);
            Assert.Contains("<hr />", xhtml, StringComparison.Ordinal);
        }
    }

    // ---- Image embedding (SC-009) ----

    [Fact]
    public async Task Every_source_image_becomes_a_real_zip_entry_under_images()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 2), ("The Mill", 1));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.epub");
            ExportResult result = await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            ZipArchiveEntry[] images =
            [
                .. archive.Entries.Where(e => e.FullName.StartsWith("OEBPS/images/", StringComparison.Ordinal))
            ];

            Assert.Equal(3, images.Length);
            Assert.Equal(3, result.EmbeddedImageCount);
        }
    }

    [Fact]
    public async Task An_embedded_image_is_byte_for_byte_the_one_in_the_encrypted_store()
    {
        // "Embedded" has to mean the actual bytes, not a placeholder of the right size.
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 2));
        await using (fixture)
        {
            IReadOnlyList<ChapterSummary> chapters = await fixture.ChapterUseCases.ListAsync(manuscriptId);
            IReadOnlyList<InlineImage> stored = await fixture.Images.ListWithBytesAsync(chapters[0].Id);

            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            List<byte[]> exported =
            [
                .. archive.Entries
                    .Where(e => e.FullName.StartsWith("OEBPS/images/", StringComparison.Ordinal))
                    .Select(BytesOf)
            ];

            foreach (InlineImage image in stored)
            {
                Assert.Contains(exported, bytes => bytes.SequenceEqual(image.Bytes));
            }
        }
    }

    [Fact]
    public async Task Image_tags_point_at_the_zip_entry_and_never_at_a_data_uri()
    {
        // E-readers handle data: URIs poorly and EPUBCheck complains, which is why the bytes are
        // unpacked into files rather than left inline (research.md §3).
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 2));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            string xhtml = TextOf(archive, "OEBPS/chapter0001.xhtml");

            Assert.DoesNotContain("data:image", xhtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("inkwell-img://", xhtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("""src="images/""", xhtml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Every_image_src_resolves_to_an_entry_that_exists()
    {
        // A rewritten path that points nowhere is worse than no rewrite: the book opens and the
        // pictures are missing.
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 2), ("The Mill", 3));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            var entryNames = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);

            foreach (ZipArchiveEntry chapter in archive.Entries
                .Where(e => e.FullName.EndsWith(".xhtml", StringComparison.Ordinal) && e.FullName.Contains("chapter", StringComparison.Ordinal))
                .ToList())
            {
                XDocument document = XDocument.Parse(TextOf(archive, chapter.FullName));
                foreach (string source in document.Descendants()
                    .Where(e => e.Name.LocalName == "img")
                    .Select(e => (string?)e.Attribute("src") ?? string.Empty))
                {
                    Assert.Contains($"OEBPS/{source}", entryNames);
                }
            }
        }
    }

    [Fact]
    public async Task Alternative_text_survives_into_the_book()
    {
        // FR-019 does not stop at the app: an exported book has screen-reader users too.
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 1));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            XDocument document = XDocument.Parse(TextOf(archive, "OEBPS/chapter0001.xhtml"));

            string? alt = document.Descendants()
                .First(e => e.Name.LocalName == "img")
                .Attribute("alt")?.Value;

            Assert.Equal("Figure 1", alt);
        }
    }

    // ---- Package structure ----

    [Fact]
    public async Task The_package_manifest_lists_every_chapter_and_every_image()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 1), ("The Mill", 2));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            XDocument opf = XDocument.Parse(TextOf(archive, "OEBPS/content.opf"));

            string[] hrefs =
            [
                .. opf.Descendants()
                    .Where(e => e.Name.LocalName == "item")
                    .Select(e => (string?)e.Attribute("href") ?? string.Empty)
            ];

            Assert.Contains("chapter0001.xhtml", hrefs);
            Assert.Contains("chapter0002.xhtml", hrefs);
            Assert.Contains("nav.xhtml", hrefs);
            Assert.Equal(3, hrefs.Count(h => h.StartsWith("images/", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task The_spine_puts_the_chapters_in_the_writers_order()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 0), ("The Mill", 0), ("The Ridge", 0));
        await using (fixture)
        {
            // Reorder before exporting: the book must follow the manuscript, not the insert order.
            IReadOnlyList<ChapterSummary> chapters = await fixture.ChapterUseCases.ListAsync(manuscriptId);
            await fixture.ChapterUseCases.ReorderAsync(
                manuscriptId, [chapters[2].Id, chapters[0].Id, chapters[1].Id]);

            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            Assert.Contains("The Ridge", TextOf(archive, "OEBPS/chapter0001.xhtml"), StringComparison.Ordinal);
            Assert.Contains("Snowfall", TextOf(archive, "OEBPS/chapter0002.xhtml"), StringComparison.Ordinal);
            Assert.Contains("The Mill", TextOf(archive, "OEBPS/chapter0003.xhtml"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Both_tables_of_contents_are_present_and_name_every_chapter()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 0), ("The Mill", 0));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);

            string nav = TextOf(archive, "OEBPS/nav.xhtml");
            Assert.Contains("Snowfall", nav, StringComparison.Ordinal);
            Assert.Contains("The Mill", nav, StringComparison.Ordinal);

            // EPUB 3 superseded the NCX, but plenty of readers in use still look for it first.
            string ncx = TextOf(archive, "OEBPS/toc.ncx");
            Assert.Contains("Snowfall", ncx, StringComparison.Ordinal);
            Assert.Contains("The Mill", ncx, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_container_points_at_the_package_document()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 0));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            XDocument container = XDocument.Parse(TextOf(archive, "META-INF/container.xml"));

            string? fullPath = container.Descendants()
                .First(e => e.Name.LocalName == "rootfile")
                .Attribute("full-path")?.Value;

            Assert.Equal("OEBPS/content.opf", fullPath);
            Assert.NotNull(archive.GetEntry(fullPath!));
        }
    }

    [Fact]
    public async Task A_title_with_xml_metacharacters_does_not_corrupt_the_package()
    {
        // A novelist may reasonably title a book "Fire & Ash <or> The Mill".
        var fixture = new StoreFixture();
        await using (fixture)
        {
            Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Fire & Ash <or> \"The Mill\"")).Value;
            await fixture.WriteChapterWithImagesAsync(manuscript.Id, "Smoke & Mirrors", 0);

            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscript.Id, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            XDocument opf = XDocument.Parse(TextOf(archive, "OEBPS/content.opf"));

            Assert.Equal(
                "Fire & Ash <or> \"The Mill\"",
                opf.Descendants().First(e => e.Name.LocalName == "title").Value);
        }
    }

    // ---- Both granularities (FR-018) ----

    [Fact]
    public async Task A_single_chapter_exports_with_its_own_images_and_nobody_elses()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 2), ("The Mill", 3));
        await using (fixture)
        {
            IReadOnlyList<ChapterSummary> chapters = await fixture.ChapterUseCases.ListAsync(manuscriptId);

            string path = fixture.ExportPath("chapter.epub");
            ExportResult result = await fixture.Export.ExportChapterAsync(chapters[0].Id, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);

            Assert.Equal(2, result.EmbeddedImageCount);
            Assert.Equal(2, archive.Entries.Count(e => e.FullName.StartsWith("OEBPS/images/", StringComparison.Ordinal)));
            Assert.Single(archive.Entries, e => e.FullName.Contains("chapter0", StringComparison.Ordinal));
            Assert.DoesNotContain("The Mill", TextOf(archive, "OEBPS/chapter0001.xhtml"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Exporting_every_chapter_separately_writes_one_file_each_in_reading_order()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 1), ("The Mill", 2), ("The Ridge", 0));
        await using (fixture)
        {
            string folder = Path.Combine(fixture.ExportDirectory, "per-chapter");

            IReadOnlyList<ExportResult> results = await fixture.Export
                .ExportManuscriptAllChaptersAsync(manuscriptId, ExportFormat.Epub, folder);

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.True(File.Exists(r.FilePath)));
            Assert.Equal([1, 2, 0], results.Select(r => r.EmbeddedImageCount));

            // Numbered so the writer's file manager shows them in reading order.
            Assert.Equal(
                ["01 - Snowfall.epub", "02 - The Mill.epub", "03 - The Ridge.epub"],
                results.Select(r => Path.GetFileName(r.FilePath)));
        }
    }

    [Fact]
    public async Task The_reported_size_matches_the_file_that_was_written()
    {
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync(("Snowfall", 1));
        await using (fixture)
        {
            string path = fixture.ExportPath("book.epub");
            ExportResult result = await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            Assert.Equal(path, result.FilePath);
            Assert.Equal(ExportFormat.Epub, result.Format);
            Assert.Equal(new FileInfo(path).Length, result.ByteLength);
            Assert.True(result.ByteLength > 0);
        }
    }

    [Fact]
    public async Task An_empty_chapter_still_produces_a_readable_book()
    {
        // Exporting a manuscript you have only just started should not be an error.
        (StoreFixture fixture, Guid manuscriptId) = await ManuscriptAsync();
        await using (fixture)
        {
            await fixture.ChapterUseCases.AddAsync(manuscriptId, "Snowfall");

            string path = fixture.ExportPath("book.epub");
            ExportResult result = await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using ZipArchive archive = Open(path);
            Assert.Equal(0, result.EmbeddedImageCount);
            Assert.Contains("Snowfall", TextOf(archive, "OEBPS/chapter0001.xhtml"), StringComparison.Ordinal);
        }
    }
}
