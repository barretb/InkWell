using System.Text;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Tests.Fixtures;

namespace InkWell.Infrastructure.Tests.Privacy;

/// <summary>
/// FR-016, FR-017, SC-002 — drafting writes only to the local encrypted store, and nothing about a
/// manuscript is legible outside it.
/// </summary>
public class DraftingPrivacyTests
{
    [Fact]
    public async Task A_full_drafting_session_leaves_no_legible_content_on_disk()
    {
        const string prose = "ElinWatchedTheMillBurnFromTheRidge";
        const string chapterTitle = "TheRidgeChapterTitle";
        const string manuscriptTitle = "AVeryPrivateNovelTitle";
        const string characterNote = "ElinIsSecretlyTheMillersDaughter";

        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync(manuscriptTitle)).Value;
        Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, chapterTitle)).Value;
        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(chapter.Id, prose, 1, fixture.Clock.Now, fixture.Clock.Today));
        await fixture.Images.AddAsync(
            new InlineImageInsert(chapter.Id, Encoding.ASCII.GetBytes(characterNote), "image/png", characterNote),
            fixture.Clock.Now);

        byte[] raw = await fixture.ReadDatabaseBytesAsync();
        string asText = Encoding.UTF8.GetString(raw);

        Assert.DoesNotContain(prose, asText, StringComparison.Ordinal);
        Assert.DoesNotContain(chapterTitle, asText, StringComparison.Ordinal);
        Assert.DoesNotContain(manuscriptTitle, asText, StringComparison.Ordinal);
        Assert.DoesNotContain(characterNote, asText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drafting_writes_only_inside_the_app_data_directory()
    {
        // FR-017: the only file the app touches while the writer drafts is its own database.
        await using var fixture = new StoreFixture();
        string directory = Path.GetDirectoryName(fixture.DatabasePath)!;

        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Winter")).Value;
        Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(chapter.Id, "The snow came early.", 4, fixture.Clock.Now, fixture.Clock.Today));

        string[] written = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        Assert.All(written, path => Assert.StartsWith(
            Path.GetFileName(fixture.DatabasePath), Path.GetFileName(path), StringComparison.Ordinal));
    }

    [Fact]
    public void The_application_layer_takes_no_networking_dependency()
    {
        // A structural guard for SC-002: if someone adds an HTTP client to the offline core, this
        // fails before any manual privacy review would catch it.
        string[] referenced = [.. typeof(Application.UseCases.ManuscriptUseCases).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)];

        Assert.DoesNotContain("System.Net.Http", referenced);
        Assert.DoesNotContain("System.Net.Sockets", referenced);
        Assert.DoesNotContain("System.Net.WebClient", referenced);
    }

    [Fact]
    public void The_infrastructure_layer_takes_no_networking_dependency()
    {
        string[] referenced = [.. typeof(Infrastructure.Persistence.ManuscriptRepository).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)];

        Assert.DoesNotContain("System.Net.Http", referenced);
        Assert.DoesNotContain("System.Net.Sockets", referenced);
    }
}
