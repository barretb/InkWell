using InkWell.Application.Abstractions;
using InkWell.Maui.UiTests.Accessibility;
using InkWell.Maui.UiTests.Harness;
using InkWell.Presentation.Controls;
using InkWell.Presentation.Services;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.UiTests;

/// <summary>
/// T124, T125 — an empty app explains itself, and a store that will not open says what happened to
/// the writer's work instead of closing the app.
/// </summary>
public class EmptyStatesAndFailuresTests
{
    // ---- Empty states (spec.md edge case "empty states") ----

    [Fact]
    public async Task An_empty_library_offers_guidance_rather_than_a_blank_screen()
    {
        await using var app = new AppHarness();

        LibraryViewModel library = app.Library;
        await library.LoadAsync();

        Assert.True(library.IsEmpty);
        Assert.Equal("No manuscripts yet.", library.StatusMessage);
    }

    [Fact]
    public async Task A_manuscript_with_no_chapters_offers_guidance()
    {
        await using var app = new AppHarness();
        Domain.Entities.Manuscript manuscript = (await app.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;

        ManuscriptViewModel screen = app.Manuscript;
        screen.ManuscriptId = manuscript.Id;
        await screen.LoadAsync();

        Assert.True(screen.IsEmpty);
        Assert.Equal("No chapters yet.", screen.StatusMessage);
    }

    [Fact]
    public void An_empty_chapter_says_the_same_thing_in_both_editors()
    {
        // The web editor's placeholder and the accessibility-mode editor's are one string, so
        // switching surfaces does not change what a new chapter tells the writer.
        string indexHtml = File.ReadAllText(
            AccessibilityHarness.PathToMauiFile("Resources", "Raw", "wwwroot", "index.html"));

        Assert.Contains(
            $"data-empty-hint=\"{AccessibleEditorDocument.EmptyChapterHint}\"",
            indexHtml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_empty_chapter_hint_says_that_saving_is_automatic()
    {
        // The one thing a writer opening a blank page most needs to be told, given there is no save
        // button anywhere in the app (FR-004).
        Assert.Contains("saved automatically", AccessibleEditorDocument.EmptyChapterHint, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Store failures (T125) ----

    [Fact]
    public async Task An_unreachable_keychain_is_explained_rather_than_crashing_the_app()
    {
        // The failure that would otherwise close the app on launch, before the writer has seen
        // anything at all (research.md §2).
        await using var app = new AppHarness();
        var failing = new FailingManuscriptRepository(new KeyStoreUnavailableException());

        var library = new LibraryViewModel(
            new Application.UseCases.ManuscriptUseCases(failing, app.Clock),
            app.Navigation,
            app.Confirmation,
            app.Errors);

        await library.LoadAsync();

        (string title, string message) = Assert.Single(app.Errors.Errors);
        Assert.Equal("InkWell cannot unlock your writing", title);
        Assert.Contains("has not been changed or deleted", message, StringComparison.Ordinal);
        Assert.Equal("InkWell could not open your writing. Nothing has been lost.", library.StatusMessage);
    }

    [Fact]
    public async Task An_unreadable_database_says_the_writing_still_exists()
    {
        await using var app = new AppHarness();
        var failing = new FailingManuscriptRepository(new IOException("The process cannot access the file."));

        var library = new LibraryViewModel(
            new Application.UseCases.ManuscriptUseCases(failing, app.Clock),
            app.Navigation,
            app.Confirmation,
            app.Errors);

        await library.LoadAsync();

        (string title, string message) = Assert.Single(app.Errors.Errors);
        Assert.Equal("InkWell could not read your writing", title);
        Assert.Contains("has not been deleted", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(KeyStoreUnavailableException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Every_explained_failure_tells_the_writer_their_work_is_safe(Type exceptionType)
    {
        // The single question someone actually has when a writing app shows an error.
        var error = (Exception)Activator.CreateInstance(exceptionType)!;

        (string title, string message) = StoreFailure.Describe(error);

        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.True(
            message.Contains("has not been changed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("has not been deleted", StringComparison.OrdinalIgnoreCase),
            $"The message for {exceptionType.Name} does not say whether the writer's work survived:\n{message}");
    }

    [Fact]
    public void An_unexpected_failure_is_not_swallowed()
    {
        // Guarding everything would turn a real bug into a screen that quietly does nothing.
        Assert.False(StoreFailure.IsExpected(new FormatException()));
        Assert.False(StoreFailure.IsExpected(new ArgumentOutOfRangeException()));
        Assert.True(StoreFailure.IsExpected(new KeyStoreUnavailableException()));
    }

    [Fact]
    public async Task A_failed_export_is_explained_and_leaves_the_manuscript_alone()
    {
        await using var app = new AppHarness();
        Domain.Entities.Manuscript manuscript = (await app.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;
        await app.ChapterUseCases.AddAsync(manuscript.Id, "Snowfall");

        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscript.Id;
        app.Picker.NextSaveLocation = Path.Combine(app.ExportDirectory, "no", "such", "folder", "book.epub");

        await export.ExportManuscriptEpubCommand.ExecuteAsync(null);

        Assert.Equal("Export failed. Your manuscript is unchanged.", export.StatusMessage);
        Assert.Single(app.Errors.Errors);
        Assert.Single(await app.ChapterUseCases.ListAsync(manuscript.Id));
    }

    /// <summary>A manuscript store that always fails the same way.</summary>
    private sealed class FailingManuscriptRepository(Exception error) : IManuscriptRepository
    {
        public Task<IReadOnlyList<Application.Abstractions.Dtos.ManuscriptSummary>> ListSummariesAsync(
            CancellationToken cancellationToken = default) => throw error;

        public Task<Domain.Entities.Manuscript?> GetAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
            => throw error;

        public Task<Application.Abstractions.Dtos.ManuscriptDetail?> GetDetailAsync(
            Guid manuscriptId, CancellationToken cancellationToken = default) => throw error;

        public Task AddAsync(Domain.Entities.Manuscript manuscript, CancellationToken cancellationToken = default)
            => throw error;

        public Task<bool> RenameAsync(
            Guid manuscriptId, string title, DateTimeOffset modifiedAt, CancellationToken cancellationToken = default)
            => throw error;

        public Task<bool> DeleteAsync(Guid manuscriptId, CancellationToken cancellationToken = default) => throw error;

        public Task TouchAsync(Guid manuscriptId, DateTimeOffset modifiedAt, CancellationToken cancellationToken = default)
            => throw error;
    }
}
