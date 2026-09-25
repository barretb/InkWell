using System.Text;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Tests.Fixtures;

namespace InkWell.Infrastructure.Tests.Privacy;

/// <summary>
/// T111 · FR-016, FR-017 — export is the one outbound path, it goes only where the writer pointed
/// it, and it leaves nothing behind on the way.
/// </summary>
/// <remarks>
/// The "no network egress" half of FR-017 is enforced a level lower, in
/// <see cref="PlatformPrivacyManifestTests"/>: the app does not request the INTERNET permission or
/// a network entitlement on any platform, so an export cannot transmit anything regardless of what
/// its code does. What is checked here is the part that is InkWell's own doing — where the bytes go,
/// and what is left over afterwards.
/// </remarks>
public class ExportPrivacyTests
{
    private const string Prose = "ElinWatchedTheMillBurnFromTheRidgeUniqueMarker";

    private static async Task<(StoreFixture Fixture, Guid ManuscriptId)> SeedAsync()
    {
        var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;
        Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Snowfall")).Value;

        await fixture.Images.AddAsync(
            new InlineImageInsert(chapter.Id, StoreFixture.PngBytes(), "image/png", "The frozen mill"),
            fixture.Clock.Now);

        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(chapter.Id, Prose, 1, fixture.Clock.Now, fixture.Clock.Today));

        return (fixture, manuscript.Id);
    }

    [Theory]
    [InlineData(ExportFormat.Epub)]
    [InlineData(ExportFormat.Pdf)]
    public async Task An_export_writes_one_file_and_only_the_one_the_writer_named(ExportFormat format)
    {
        (StoreFixture fixture, Guid manuscriptId) = await SeedAsync();
        await using (fixture)
        {
            string folder = Path.Combine(fixture.ExportDirectory, "watched");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "book" + (format == ExportFormat.Epub ? ".epub" : ".pdf"));

            await fixture.Export.ExportManuscriptAsync(manuscriptId, format, path);

            // No scratch copy, no sidecar, no temporary file left behind — an export that scattered
            // plaintext around the file system would defeat the encrypted store it came from.
            string[] written = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            Assert.Equal([path], written);
        }
    }

    [Fact]
    public async Task Export_writes_nothing_beside_the_database()
    {
        // The app's own data directory should be untouched by an export; the only new bytes belong
        // in the folder the writer chose.
        (StoreFixture fixture, Guid manuscriptId) = await SeedAsync();
        await using (fixture)
        {
            string databaseFolder = Path.GetDirectoryName(fixture.DatabasePath)!;
            string[] before = Directory.GetFiles(databaseFolder);

            await fixture.Export.ExportManuscriptAsync(
                manuscriptId, ExportFormat.Epub, fixture.ExportPath("book.epub"));

            string[] after = Directory.GetFiles(databaseFolder);
            Assert.Equal(before.OrderBy(f => f, StringComparer.Ordinal), after.OrderBy(f => f, StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task Exporting_does_not_make_the_store_readable()
    {
        // Reading prose out of the database for an export must not leave the file itself decrypted.
        (StoreFixture fixture, Guid manuscriptId) = await SeedAsync();
        await using (fixture)
        {
            await fixture.Export.ExportManuscriptAsync(
                manuscriptId, ExportFormat.Pdf, fixture.ExportPath("book.pdf"));

            byte[] raw = await fixture.ReadDatabaseBytesAsync();
            string asText = Encoding.UTF8.GetString(raw);

            Assert.DoesNotContain(Prose, asText, StringComparison.Ordinal);
            Assert.DoesNotContain("The Long Winter", asText, StringComparison.Ordinal);
            Assert.False(asText.StartsWith("SQLite format 3", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_batch_export_writes_only_into_the_folder_the_writer_chose()
    {
        (StoreFixture fixture, Guid manuscriptId) = await SeedAsync();
        await using (fixture)
        {
            await fixture.ChapterUseCases.AddAsync(manuscriptId, "The Mill");
            string folder = Path.Combine(fixture.ExportDirectory, "batch");

            IReadOnlyList<ExportResult> results = await fixture.Export
                .ExportManuscriptAllChaptersAsync(manuscriptId, ExportFormat.Epub, folder);

            Assert.All(results, r => Assert.StartsWith(
                folder + Path.DirectorySeparatorChar, r.FilePath, StringComparison.Ordinal));
            Assert.Equal(results.Count, Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length);
        }
    }

    [Fact]
    public async Task An_export_is_the_writers_prose_and_not_a_copy_of_the_encrypted_file()
    {
        // The counterpart of the assertions above: the export really does contain the work, in the
        // clear, because the writer asked for a readable book. That is the whole bargain of FR-017 —
        // content leaves only when they say so, and then it is theirs to read.
        (StoreFixture fixture, Guid manuscriptId) = await SeedAsync();
        await using (fixture)
        {
            string path = fixture.ExportPath("book.epub");
            await fixture.Export.ExportManuscriptAsync(manuscriptId, ExportFormat.Epub, path);

            using System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.OpenRead(path);
            using var reader = new StreamReader(archive.GetEntry("OEBPS/chapter0001.xhtml")!.Open());

            Assert.Contains(Prose, await reader.ReadToEndAsync(), StringComparison.Ordinal);
        }
    }
}
