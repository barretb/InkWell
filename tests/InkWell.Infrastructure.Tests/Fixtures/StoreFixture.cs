using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Application.Tests.Fakes;
using InkWell.Application.UseCases;
using InkWell.Infrastructure.Persistence;

namespace InkWell.Infrastructure.Tests.Fixtures;

/// <summary>
/// A fully wired store — repositories and use cases over a real keyed database — so story tests can
/// exercise the same code path the app uses instead of a mock stack.
/// </summary>
public sealed class StoreFixture : IAsyncDisposable
{
    private readonly KeyedDatabaseFixture _database = new();

    /// <summary>Creates the fixture with a controllable clock.</summary>
    public StoreFixture()
    {
        Clock = new FixedClock();
        Images = new InlineImageRepository(_database.Factory);
        Manuscripts = new ManuscriptRepository(_database.Factory);
        Chapters = new ChapterRepository(_database.Factory, Images);
        ManuscriptUseCases = new ManuscriptUseCases(Manuscripts, Clock);
        ChapterUseCases = new ChapterUseCases(Chapters, Manuscripts, Clock);
        GoalUseCases = new GoalUseCases(
            new DailyGoalRepository(_database.Factory),
            new WritingHistoryRepository(_database.Factory),
            Clock);
        ReferenceUseCases = new ReferenceUseCases(
            new ReferenceRepository(_database.Factory), Manuscripts, Clock);
    }

    /// <summary>The test's controllable clock.</summary>
    public FixedClock Clock { get; }

    /// <summary>The manuscript repository under test.</summary>
    public IManuscriptRepository Manuscripts { get; private set; }

    /// <summary>The chapter repository under test.</summary>
    public IChapterRepository Chapters { get; private set; }

    /// <summary>The inline-image repository under test.</summary>
    public IInlineImageRepository Images { get; private set; }

    /// <summary>Manuscript orchestration.</summary>
    public ManuscriptUseCases ManuscriptUseCases { get; private set; }

    /// <summary>Chapter orchestration.</summary>
    public ChapterUseCases ChapterUseCases { get; private set; }

    /// <summary>Goal and writing-history orchestration.</summary>
    public GoalUseCases GoalUseCases { get; private set; }

    /// <summary>Character and plot-thread orchestration.</summary>
    public ReferenceUseCases ReferenceUseCases { get; private set; }

    /// <summary>
    /// Writes a chapter whose prose comes to exactly <paramref name="totalWords"/> words, through
    /// the same transactional autosave path the editor uses.
    /// </summary>
    /// <remarks>
    /// Goal progress is driven by the change in a chapter's count, so a test that wants "the writer
    /// now has 500 words" has to go through a real save rather than poking the history table.
    /// </remarks>
    public async Task WriteWordsAsync(Guid manuscriptId, int totalWords, string? chapterTitle = null)
    {
        IReadOnlyList<ChapterSummary> chapters = await ChapterUseCases.ListAsync(manuscriptId).ConfigureAwait(false);
        Guid chapterId = chapterTitle is null
            ? chapters[0].Id
            : chapters.FirstOrDefault(c => c.Title == chapterTitle)?.Id
              ?? (await ChapterUseCases.AddAsync(manuscriptId, chapterTitle).ConfigureAwait(false)).Value.Id;

        string markdown = totalWords == 0 ? string.Empty : string.Join(' ', Enumerable.Repeat("word", totalWords));

        await Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(chapterId, markdown, totalWords, Clock.Now, Clock.Today)).ConfigureAwait(false);
    }

    /// <summary>Where the encrypted database file lives.</summary>
    public string DatabasePath => _database.DatabasePath;

    /// <summary>Reads the raw database file while it is still open.</summary>
    public Task<byte[]> ReadDatabaseBytesAsync() => _database.ReadDatabaseBytesAsync();

    /// <summary>
    /// Closes and reopens the whole stack against the same encrypted file — the test equivalent of
    /// quitting and relaunching the app.
    /// </summary>
    public async Task RestartAsync()
    {
        await _database.RestartAsync().ConfigureAwait(false);
        Images = new InlineImageRepository(_database.Factory);
        Manuscripts = new ManuscriptRepository(_database.Factory);
        Chapters = new ChapterRepository(_database.Factory, Images);
        ManuscriptUseCases = new ManuscriptUseCases(Manuscripts, Clock);
        ChapterUseCases = new ChapterUseCases(Chapters, Manuscripts, Clock);
        GoalUseCases = new GoalUseCases(
            new DailyGoalRepository(_database.Factory),
            new WritingHistoryRepository(_database.Factory),
            Clock);
        ReferenceUseCases = new ReferenceUseCases(
            new ReferenceRepository(_database.Factory), Manuscripts, Clock);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
