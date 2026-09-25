using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Tests.Fixtures;
using Xunit.Abstractions;

namespace InkWell.Infrastructure.Tests.Export;

/// <summary>
/// T120 · SC-009 — exported books are validated by the W3C's own EPUBCheck, not only by this
/// project's idea of what an EPUB should look like.
/// </summary>
/// <remarks>
/// EPUBCheck is a Java tool and is not vendored here, so these tests report plainly when it is
/// absent instead of passing quietly. Setting <c>INKWELL_REQUIRE_EPUBCHECK=1</c> — which CI does —
/// turns its absence into a failure, so the validation step cannot go missing without anyone
/// noticing.
/// </remarks>
public class EpubCheckTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [EpubCheckFact]
    public void EpubCheck_is_installed_where_the_build_requires_it()
    {
        if (EpubCheckValidator.IsAvailable)
        {
            return;
        }

        Assert.False(
            EpubCheckValidator.IsRequired,
            $"{EpubCheckValidator.RequireVariable} is set, but EPUBCheck could not be run. " +
            EpubCheckValidator.UnavailableReason);

        _output.WriteLine(
            "EPUBCheck validation did not run. " + EpubCheckValidator.UnavailableReason +
            $" Set {EpubCheckValidator.RequireVariable}=1 to make this a failure.");
    }

    [EpubCheckFact]
    public async Task A_whole_manuscript_validates_as_an_epub()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;
        await fixture.WriteChapterWithImagesAsync(manuscript.Id, "Snowfall", 2);
        await fixture.WriteChapterWithImagesAsync(manuscript.Id, "The Mill", 1);

        string path = fixture.ExportPath("book.epub");
        await fixture.Export.ExportManuscriptAsync(manuscript.Id, ExportFormat.Epub, path);

        await AssertValidAsync(path);
    }

    [EpubCheckFact]
    public async Task A_single_chapter_validates_as_an_epub()
    {
        // Per-chapter export is the same generator over a different chapter list (FR-018), but the
        // package it produces is a different shape — one spine item, one nav entry — so it is
        // validated in its own right.
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;
        Guid chapterId = await fixture.WriteChapterWithImagesAsync(manuscript.Id, "Snowfall", 1);

        string path = fixture.ExportPath("chapter.epub");
        await fixture.Export.ExportChapterAsync(chapterId, ExportFormat.Epub, path);

        await AssertValidAsync(path);
    }

    [EpubCheckFact]
    public async Task A_manuscript_with_markdown_a_writer_would_actually_type_validates()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;
        Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Snowfall")).Value;

        const string markdown = """
            # The ridge

            Elin watched the **mill** burn from the ridge. She had *not* meant for it to happen.

            > "It was already burning," she said.

            - Her father's ledger
            - The missing key
            - A footprint in the ash

            ---

            Line one
            Line two

            A [link](https://example.invalid/) and some `code`.
            """;

        await fixture.Chapters.CommitAutoSaveAsync(new AutoSaveCommit(
            chapter.Id, markdown, 40, fixture.Clock.Now, fixture.Clock.Today));

        string path = fixture.ExportPath("prose.epub");
        await fixture.Export.ExportManuscriptAsync(manuscript.Id, ExportFormat.Epub, path);

        await AssertValidAsync(path);
    }

    private async Task AssertValidAsync(string path)
    {
        Assert.True(EpubCheckValidator.IsAvailable, EpubCheckValidator.UnavailableReason);

        EpubCheckResult result = await EpubCheckValidator.ValidateAsync(path);
        _output.WriteLine(result.Report);

        Assert.True(result.IsValid, $"EPUBCheck rejected {Path.GetFileName(path)}:\n{result.Report}");
    }
}

/// <summary>Records unavailable optional validation as skipped, while required validation fails.</summary>
public sealed class EpubCheckFactAttribute : FactAttribute
{
    public EpubCheckFactAttribute()
    {
        if (!EpubCheckValidator.IsRequired && !EpubCheckValidator.IsAvailable)
        {
            Skip = EpubCheckValidator.UnavailableReason;
        }
    }
}
