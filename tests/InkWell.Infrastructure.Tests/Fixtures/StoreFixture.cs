using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Application.Tests.Fakes;
using InkWell.Application.UseCases;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Export;
using InkWell.Infrastructure.Markdown;
using InkWell.Infrastructure.Persistence;

namespace InkWell.Infrastructure.Tests.Fixtures;

/// <summary>
/// A fully wired store — repositories and use cases over a real keyed database — so story tests can
/// exercise the same code path the app uses instead of a mock stack.
/// </summary>
public sealed class StoreFixture : IAsyncDisposable
{
    private readonly KeyedDatabaseFixture _database = new();

    private readonly string _exportDirectory =
        Path.Combine(Path.GetTempPath(), "inkwell-exports", Guid.NewGuid().ToString("N"));

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
        Export = new ExportService(Manuscripts, Chapters, Images, new MarkdownService());
        DataControls = new DataControlsRepository(_database.Factory, _database.Paths);
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

    /// <summary>EPUB and PDF export, over the same store (FR-018).</summary>
    public IExportService Export { get; private set; }

    /// <summary>View-all and delete-all data controls (FR-018, SC-008).</summary>
    public IDataControlsRepository DataControls { get; private set; }

    /// <summary>The key store behind the encrypted database.</summary>
    public IKeyStore KeyStore => _database.KeyStore;

    /// <summary>The secure storage the database key lives in, so a test can see whether it survived.</summary>
    public InMemorySecureStore SecureStore => _database.SecureStore;

    /// <summary>A directory this test may write exports into; removed with the fixture.</summary>
    public string ExportDirectory
    {
        get
        {
            Directory.CreateDirectory(_exportDirectory);
            return _exportDirectory;
        }
    }

    /// <summary>A path inside <see cref="ExportDirectory"/> for one exported file.</summary>
    public string ExportPath(string fileName) => Path.Combine(ExportDirectory, fileName);

    /// <summary>
    /// Writes a chapter containing <paramref name="imageCount"/> embedded images and some prose,
    /// returning the chapter's id.
    /// </summary>
    /// <remarks>
    /// Images are inserted through the real repository so their bytes genuinely live in the
    /// encrypted store, which is the only way an export test can prove it pulled them out of it.
    /// </remarks>
    public async Task<Guid> WriteChapterWithImagesAsync(
        Guid manuscriptId,
        string chapterTitle,
        int imageCount,
        string prose = "Elin watched the mill burn from the ridge.")
    {
        Chapter chapter = (await ChapterUseCases.AddAsync(manuscriptId, chapterTitle).ConfigureAwait(false)).Value;

        var markdown = new System.Text.StringBuilder(prose);
        for (int i = 0; i < imageCount; i++)
        {
            // Alternating formats, so one export exercises both the extension mapping and the
            // media-type entries in the EPUB manifest.
            (byte[] bytes, string mimeType) = i % 2 == 0 ? (PngBytes(), "image/png") : (GifBytes(), "image/gif");

            InlineImageReference reference = await Images.AddAsync(
                new InlineImageInsert(chapter.Id, bytes, mimeType, $"Figure {i + 1}"),
                Clock.Now).ConfigureAwait(false);

            markdown.Append("\n\n![").Append(reference.AltText).Append("](inkwell-img://").Append(reference.Id).Append(')');
        }

        string content = markdown.ToString();
        await Chapters.CommitAutoSaveAsync(new AutoSaveCommit(
            chapter.Id,
            content,
            Domain.Services.ProseWordCounter.Count(content),
            Clock.Now,
            Clock.Today)).ConfigureAwait(false);

        return chapter.Id;
    }

    /// <summary>A genuinely valid 1×1 PNG.</summary>
    /// <remarks>
    /// Real image bytes rather than random data, because the PDF exporter hands them to PDFsharp,
    /// which decodes them — random bytes would exercise the plumbing and not the embedding.
    /// </remarks>
    public static byte[] PngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>A genuinely valid 1×1 GIF, so a test can tell two embedded images apart.</summary>
    public static byte[] GifBytes() => Convert.FromBase64String(
        "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

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
        Export = new ExportService(Manuscripts, Chapters, Images, new MarkdownService());
        DataControls = new DataControlsRepository(_database.Factory, _database.Paths);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync().ConfigureAwait(false);

        try
        {
            if (Directory.Exists(_exportDirectory))
            {
                Directory.Delete(_exportDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A lingering handle on an exported file must never fail a test run.
        }
    }
}
