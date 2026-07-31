using InkWell.Application.Abstractions;
using InkWell.Application.Tests.Fakes;
using InkWell.Application.UseCases;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Persistence;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.UiTests.Harness;

/// <summary>
/// The whole app minus its window: real ViewModels, real use cases, and a real SQLCipher database in
/// a temporary directory.
/// </summary>
/// <remarks>
/// Only the device-bound edges are substituted — the editor surface and the three prompt services.
/// Everything a user story asserts about behaviour runs through the code that ships.
/// </remarks>
public sealed class AppHarness : IAsyncDisposable
{
    private readonly string _directory;
    private readonly FakeKeyStore _keyStore = new();
    private SqlCipherConnectionFactory _factory;
    private EditorViewModel? _editor;

    /// <summary>Creates the harness with an empty database.</summary>
    /// <param name="autoSaveDebounce">
    /// How long typing must pause before a commit. Tests that assert the debounce use a short
    /// interval; tests that flush explicitly use a long one so nothing commits behind their back.
    /// </param>
    public AppHarness(TimeSpan? autoSaveDebounce = null)
    {
        _directory = Path.Combine(Path.GetTempPath(), "inkwell-app", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        AutoSaveOptions = new AutoSaveOptions(autoSaveDebounce ?? TimeSpan.FromMinutes(10));
        _factory = new SqlCipherConnectionFactory(_keyStore, new Paths(_directory));
        Wire();
    }

    /// <summary>The test's controllable clock.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>The autosave tuning in force.</summary>
    public AutoSaveOptions AutoSaveOptions { get; }

    /// <summary>The stand-in editor surface.</summary>
    public FakeEditorHost EditorHost { get; } = new();

    /// <summary>The confirmation dialog the writer sees.</summary>
    public FakeConfirmationService Confirmation { get; } = new();

    /// <summary>Errors the writer was shown.</summary>
    public FakeErrorPresenter Errors { get; } = new();

    /// <summary>Where the writer navigated.</summary>
    public FakeNavigationService Navigation { get; } = new();

    /// <summary>Manuscript orchestration.</summary>
    public ManuscriptUseCases ManuscriptUseCases { get; private set; } = null!;

    /// <summary>Chapter orchestration.</summary>
    public ChapterUseCases ChapterUseCases { get; private set; } = null!;

    /// <summary>The inline-image store.</summary>
    public IInlineImageRepository Images { get; private set; } = null!;

    /// <summary>The chapter store.</summary>
    public IChapterRepository ChapterRepository { get; private set; } = null!;

    /// <summary>The library screen.</summary>
    public LibraryViewModel Library => new(ManuscriptUseCases, Navigation, Confirmation, Errors);

    /// <summary>The manuscript screen.</summary>
    public ManuscriptViewModel Manuscript =>
        new(ManuscriptUseCases, ChapterUseCases, Navigation, Confirmation, Errors);

    /// <summary>Goal and writing-history orchestration.</summary>
    public GoalUseCases GoalUseCases { get; private set; } = null!;

    /// <summary>The daily-goal screen.</summary>
    public GoalsViewModel Goals => new(GoalUseCases, Confirmation, Errors);

    /// <summary>Character and plot-thread orchestration.</summary>
    public ReferenceUseCases ReferenceUseCases { get; private set; } = null!;

    /// <summary>The characters screen.</summary>
    public CharactersViewModel Characters => new(ReferenceUseCases, Confirmation, Errors);

    /// <summary>The plot-threads screen.</summary>
    public PlotThreadsViewModel PlotThreads => new(ReferenceUseCases, Confirmation, Errors);

    /// <summary>
    /// The editor screen, attached to <see cref="EditorHost"/>. Created once per harness so a test
    /// that toggles focus mode is talking to the same view model that autosaved its typing.
    /// </summary>
    public EditorViewModel Editor
    {
        get
        {
            if (_editor is null)
            {
                _editor = new EditorViewModel(
                    ChapterUseCases,
                    GoalUseCases,
                    Images,
                    new AutoSaveCoordinator(ChapterRepository, Clock, AutoSaveOptions),
                    Clock,
                    Navigation,
                    Errors);
                _editor.Attach(EditorHost);
            }

            return _editor;
        }
    }

    /// <summary>Creates a manuscript with one chapter and opens it in the editor.</summary>
    public async Task<(Guid ManuscriptId, Guid ChapterId)> OpenNewChapterAsync(
        string manuscriptTitle = "The Long Winter",
        string chapterTitle = "Snowfall")
    {
        Manuscript manuscript = (await ManuscriptUseCases.CreateAsync(manuscriptTitle)).Value;
        Chapter chapter = (await ChapterUseCases.AddAsync(manuscript.Id, chapterTitle)).Value;

        Editor.ChapterId = chapter.Id;
        Editor.ManuscriptId = manuscript.Id;
        await Editor.LoadAsync();

        return (manuscript.Id, chapter.Id);
    }

    /// <summary>Reads a chapter's stored markdown straight from the database.</summary>
    public async Task<string> ReadStoredMarkdownAsync(Guid chapterId)
        => (await ChapterUseCases.GetContentAsync(chapterId)).Value.ContentMarkdown;

    /// <summary>Closes and relaunches against the same encrypted file.</summary>
    public async Task RestartAsync()
    {
        if (_editor is not null)
        {
            await _editor.DisposeAsync();
            _editor = null;
        }

        await _factory.CheckpointAsync();
        await _factory.DisposeAsync();
        _factory = new SqlCipherConnectionFactory(_keyStore, new Paths(_directory));
        Wire();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_editor is not null)
        {
            await _editor.DisposeAsync();
        }

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
        Images = new InlineImageRepository(_factory);
        var manuscripts = new ManuscriptRepository(_factory);
        ChapterRepository = new ChapterRepository(_factory, Images);
        ManuscriptUseCases = new ManuscriptUseCases(manuscripts, Clock);
        ChapterUseCases = new ChapterUseCases(ChapterRepository, manuscripts, Clock);
        GoalUseCases = new GoalUseCases(
            new DailyGoalRepository(_factory),
            new WritingHistoryRepository(_factory),
            Clock);
        ReferenceUseCases = new ReferenceUseCases(new ReferenceRepository(_factory), manuscripts, Clock);
    }

    private sealed record Paths(string Directory) : IAppStoragePaths
    {
        public string DatabaseFilePath => Path.Combine(Directory, "inkwell.db3");
    }
}
