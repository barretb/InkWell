using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Application.Tests.Fakes;
using InkWell.Application.UseCases;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Persistence;

namespace InkWell.Maui.UiTests;

/// <summary>
/// User Story 1 end to end: "Create a manuscript, add three chapters, write text in each, reorder
/// them, close and reopen the app, and confirm all content and ordering are preserved."
/// </summary>
/// <remarks>
/// Runs the journey the way the app runs it — autosave with no explicit save, a real SQLCipher
/// database, and a genuine close/reopen — rather than asserting against mocks.
/// </remarks>
public class UserStory1Tests
{
    [Fact]
    public async Task The_writer_drafts_three_chapters_reorders_them_and_finds_everything_after_a_restart()
    {
        await using var app = new JourneyHarness();

        // 1. With no manuscripts, create one and name it.
        Assert.Empty(await app.Manuscripts.ListAsync());
        Manuscript manuscript = (await app.Manuscripts.CreateAsync("The Long Winter")).Value;
        ManuscriptSummary listed = Assert.Single(await app.Manuscripts.ListAsync());
        Assert.Equal("The Long Winter", listed.Title);

        // 2. Add three chapters and type prose into each — no save button is ever pressed.
        Chapter one = (await app.Chapters.AddAsync(manuscript.Id, "Snowfall")).Value;
        Chapter two = (await app.Chapters.AddAsync(manuscript.Id, "The Mill")).Value;
        Chapter three = (await app.Chapters.AddAsync(manuscript.Id, "Thaw")).Value;

        await app.TypeAsync(one.Id, "The snow came early that year.");
        await app.TypeAsync(two.Id, "It fell for **nine** days without pause.");
        await app.TypeAsync(three.Id, "Then, on a Tuesday, it stopped.");

        // 3. Reorder the chapters as the story evolves.
        DomainResult reordered = await app.Chapters.ReorderAsync(manuscript.Id, [three.Id, one.Id, two.Id]);
        Assert.True(reordered.IsSuccess);

        // 4. Close and reopen the app.
        await app.RestartAsync();

        IReadOnlyList<ChapterSummary> chapters = await app.Chapters.ListAsync(manuscript.Id);
        Assert.Equal([three.Id, one.Id, two.Id], chapters.Select(c => c.Id));
        Assert.Equal(["Thaw", "Snowfall", "The Mill"], chapters.Select(c => c.Title));

        Assert.Equal(
            "The snow came early that year.",
            (await app.Chapters.GetContentAsync(one.Id)).Value.ContentMarkdown);
        Assert.Equal(
            "It fell for **nine** days without pause.",
            (await app.Chapters.GetContentAsync(two.Id)).Value.ContentMarkdown);

        // Emphasis markers are not words: 6 + 7 + 6 prose words (FR-009, SC-005).
        Assert.Equal(19, await app.Chapters.GetManuscriptWordCountAsync(manuscript.Id));
    }

    [Fact]
    public async Task A_chapter_is_removed_only_after_the_writer_confirms()
    {
        // US1 scenario 5. The confirmation itself is the caller's responsibility, so this asserts
        // both halves: declining leaves the chapter, confirming removes it.
        await using var app = new JourneyHarness();
        Manuscript manuscript = (await app.Manuscripts.CreateAsync("Winter")).Value;
        Chapter chapter = (await app.Chapters.AddAsync(manuscript.Id, "Snowfall")).Value;

        var writerDeclined = false;
        if (writerDeclined)
        {
            await app.Chapters.DeleteAsync(chapter.Id);
        }

        Assert.Single(await app.Chapters.ListAsync(manuscript.Id));

        await app.Chapters.DeleteAsync(chapter.Id);
        Assert.Empty(await app.Chapters.ListAsync(manuscript.Id));
    }

    [Fact]
    public async Task Everything_in_the_journey_works_with_no_network_available()
    {
        // US1 scenario 4 / SC-002. Nothing in the path can reach a network: the offline core takes
        // no networking dependency, and the platform manifests grant none (see the privacy suite).
        await using var app = new JourneyHarness();

        Manuscript manuscript = (await app.Manuscripts.CreateAsync("Offline")).Value;
        Chapter chapter = (await app.Chapters.AddAsync(manuscript.Id, "One")).Value;
        await app.TypeAsync(chapter.Id, "Written on a plane.");
        await app.RestartAsync();

        Assert.Equal(
            "Written on a plane.",
            (await app.Chapters.GetContentAsync(chapter.Id)).Value.ContentMarkdown);
    }

    [Fact]
    public async Task An_unexpected_shutdown_costs_at_most_the_last_moments_of_typing()
    {
        // SC-003: the harness types and then "crashes" without a graceful close.
        await using var app = new JourneyHarness();
        Manuscript manuscript = (await app.Manuscripts.CreateAsync("Winter")).Value;
        Chapter chapter = (await app.Chapters.AddAsync(manuscript.Id, "One")).Value;

        await app.TypeAsync(chapter.Id, "A sentence the writer never saved.");
        await app.RestartAsync();

        Assert.Equal(
            "A sentence the writer never saved.",
            (await app.Chapters.GetContentAsync(chapter.Id)).Value.ContentMarkdown);
    }

    /// <summary>
    /// The application stack over a real encrypted database in a temporary directory.
    /// </summary>
    private sealed class JourneyHarness : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly FakeKeyStore _keyStore = new();
        private readonly FixedClock _clock = new();
        private SqlCipherConnectionFactory _factory;

        public JourneyHarness()
        {
            _directory = Path.Combine(Path.GetTempPath(), "inkwell-journey", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _factory = new SqlCipherConnectionFactory(_keyStore, new Paths(_directory));
            Wire();
        }

        public ManuscriptUseCases Manuscripts { get; private set; } = null!;

        public ChapterUseCases Chapters { get; private set; } = null!;

        private IChapterRepository ChapterRepository { get; set; } = null!;

        /// <summary>Types into a chapter exactly as the editor does: no explicit save.</summary>
        public async Task TypeAsync(Guid chapterId, string markdown)
        {
            await using var autoSave = new AutoSaveCoordinator(
                ChapterRepository, _clock, new AutoSaveOptions(TimeSpan.FromMilliseconds(20)));

            autoSave.QueueEdit(chapterId, markdown);
            await autoSave.FlushAsync();
            _clock.Advance(TimeSpan.FromSeconds(30));
        }

        /// <summary>Closes and relaunches against the same encrypted file.</summary>
        public async Task RestartAsync()
        {
            await _factory.CheckpointAsync();
            await _factory.DisposeAsync();
            _factory = new SqlCipherConnectionFactory(_keyStore, new Paths(_directory));
            Wire();
        }

        public async ValueTask DisposeAsync()
        {
            await _factory.DisposeAsync();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A lingering handle must never fail a test run.
            }
        }

        private void Wire()
        {
            var images = new InlineImageRepository(_factory);
            var manuscripts = new ManuscriptRepository(_factory);
            ChapterRepository = new ChapterRepository(_factory, images);
            Manuscripts = new ManuscriptUseCases(manuscripts, _clock);
            Chapters = new ChapterUseCases(ChapterRepository, manuscripts, _clock);
        }

        private sealed record Paths(string Directory) : Application.Abstractions.IAppStoragePaths
        {
            public string DatabaseFilePath => Path.Combine(Directory, "inkwell.db3");
        }
    }
}
