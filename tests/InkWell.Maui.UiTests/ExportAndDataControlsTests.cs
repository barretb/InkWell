using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Maui.UiTests.Harness;
using InkWell.Presentation;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.UiTests;

/// <summary>
/// T117, T118 · FR-018, SC-008, SC-009 — the two screens that make the app's promise actionable:
/// take your writing with you, and take it off this device when you want to.
/// </summary>
public class ExportAndDataControlsTests
{
    private static async Task<Guid> SeedAsync(AppHarness app, string title = "The Long Winter")
    {
        Manuscript manuscript = (await app.ManuscriptUseCases.CreateAsync(title)).Value;

        Chapter one = (await app.ChapterUseCases.AddAsync(manuscript.Id, "Snowfall")).Value;
        Chapter two = (await app.ChapterUseCases.AddAsync(manuscript.Id, "The Mill")).Value;

        await app.ChapterRepository.CommitAutoSaveAsync(new AutoSaveCommit(
            one.Id, "Elin watched the mill burn from the ridge.", 8, app.Clock.Now, app.Clock.Today));
        await app.ChapterRepository.CommitAutoSaveAsync(new AutoSaveCommit(
            two.Id, "The mill had stood for two hundred years.", 8, app.Clock.Now, app.Clock.Today));

        await app.ReferenceUseCases.CreateCharacterAsync(manuscript.Id, "Elin", "Miller's daughter.");
        await app.GoalUseCases.SetGoalAsync(manuscript.Id, 500);

        return manuscript.Id;
    }

    // ---- Export: nothing leaves without a destination the writer chose (FR-017) ----

    [Fact]
    public async Task Cancelling_the_save_dialog_exports_nothing()
    {
        await using var app = new AppHarness();
        Guid manuscriptId = await SeedAsync(app);

        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscriptId;
        export.ManuscriptTitle = "The Long Winter";
        app.Picker.NextSaveLocation = null;

        await export.ExportManuscriptEpubCommand.ExecuteAsync(null);

        Assert.Equal("Export cancelled.", export.StatusMessage);
        Assert.Null(export.LastResult);
        Assert.Empty(Directory.GetFiles(app.ExportDirectory));
    }

    [Fact]
    public async Task An_export_writes_only_to_the_path_the_dialog_returned()
    {
        await using var app = new AppHarness();
        Guid manuscriptId = await SeedAsync(app);

        string destination = Path.Combine(app.ExportDirectory, "chosen-name.epub");
        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscriptId;
        export.ManuscriptTitle = "The Long Winter";
        app.Picker.NextSaveLocation = destination;

        await export.ExportManuscriptEpubCommand.ExecuteAsync(null);

        Assert.Equal(destination, export.LastResult!.FilePath);
        Assert.Equal([destination], Directory.GetFiles(app.ExportDirectory));
    }

    [Fact]
    public async Task The_suggested_file_name_comes_from_the_manuscript_title()
    {
        await using var app = new AppHarness();
        Guid manuscriptId = await SeedAsync(app);

        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscriptId;
        export.ManuscriptTitle = "The Long Winter";
        app.Picker.NextSaveLocation = null;

        await export.ExportManuscriptEpubCommand.ExecuteAsync(null);
        await export.ExportManuscriptPdfCommand.ExecuteAsync(null);

        Assert.Equal(["The Long Winter.epub", "The Long Winter.pdf"], app.Picker.SuggestedNames);
    }

    [Fact]
    public async Task A_title_a_file_system_would_refuse_still_produces_a_usable_suggestion()
    {
        await using var app = new AppHarness();
        Guid manuscriptId = await SeedAsync(app);

        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscriptId;
        export.ManuscriptTitle = "Fire & Ash: The Mill/Ridge?";
        app.Picker.NextSaveLocation = null;

        await export.ExportManuscriptEpubCommand.ExecuteAsync(null);

        string suggested = Assert.Single(app.Picker.SuggestedNames);
        Assert.DoesNotContain(suggested, name => Path.GetInvalidFileNameChars().Contains(name));
        Assert.EndsWith(".epub", suggested, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExportFormat.Epub, "book.epub")]
    [InlineData(ExportFormat.Pdf, "book.pdf")]
    public async Task Both_formats_export_the_whole_manuscript(ExportFormat format, string fileName)
    {
        await using var app = new AppHarness();
        Guid manuscriptId = await SeedAsync(app);

        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscriptId;
        app.Picker.NextSaveLocation = Path.Combine(app.ExportDirectory, fileName);

        await export.ExportManuscriptAsync(format);

        Assert.NotNull(export.LastResult);
        Assert.Equal(format, export.LastResult!.Format);
        Assert.True(File.Exists(export.LastResult.FilePath));
        Assert.Contains("Saved to", export.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exporting_one_chapter_offers_that_chapter_and_no_other()
    {
        await using var app = new AppHarness();
        Guid manuscriptId = await SeedAsync(app);
        IReadOnlyList<ChapterSummary> chapters = await app.ChapterUseCases.ListAsync(manuscriptId);

        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscriptId;
        export.ChapterId = chapters[0].Id;
        export.ChapterTitle = "Snowfall";
        app.Picker.NextSaveLocation = Path.Combine(app.ExportDirectory, "one.epub");

        Assert.True(export.HasChapter);
        await export.ExportChapterEpubCommand.ExecuteAsync(null);

        Assert.Equal("Snowfall.epub", app.Picker.SuggestedNames[0]);
        Assert.True(File.Exists(export.LastResult!.FilePath));
    }

    [Fact]
    public async Task The_single_chapter_option_is_hidden_when_no_chapter_is_open()
    {
        // Arriving from the library there is no chapter, and offering a dead control would be worse
        // than offering nothing.
        await using var app = new AppHarness();
        ExportViewModel export = app.Export;

        Assert.False(export.HasChapter);

        export.ChapterId = Guid.NewGuid();
        Assert.True(export.HasChapter);
    }

    [Fact]
    public async Task Exporting_every_chapter_asks_for_a_folder_and_fills_it()
    {
        await using var app = new AppHarness();
        Guid manuscriptId = await SeedAsync(app);

        string folder = Path.Combine(app.ExportDirectory, "per-chapter");
        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscriptId;
        app.Picker.NextFolder = folder;

        await export.ExportEachChapterEpubCommand.ExecuteAsync(null);

        Assert.Equal(1, app.Picker.FolderPromptCount);
        Assert.Equal(2, Directory.GetFiles(folder).Length);
        Assert.Contains("Exported 2 chapters", export.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_the_folder_dialog_exports_nothing()
    {
        await using var app = new AppHarness();
        Guid manuscriptId = await SeedAsync(app);

        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscriptId;
        app.Picker.NextFolder = null;

        await export.ExportEachChapterPdfCommand.ExecuteAsync(null);

        Assert.Equal("Export cancelled.", export.StatusMessage);
        Assert.Empty(Directory.GetDirectories(app.ExportDirectory));
    }

    [Fact]
    public async Task A_failed_export_says_the_manuscript_is_unharmed()
    {
        // Someone whose export just failed needs to know their novel is still there.
        await using var app = new AppHarness();
        Guid manuscriptId = await SeedAsync(app);

        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscriptId;

        // A path inside a directory that does not exist: the write fails, the store does not.
        app.Picker.NextSaveLocation = Path.Combine(app.ExportDirectory, "missing", "deeper", "book.epub");

        await export.ExportManuscriptEpubCommand.ExecuteAsync(null);

        Assert.Equal("Export failed. Your manuscript is unchanged.", export.StatusMessage);
        Assert.Single(app.Errors.Errors);
        Assert.Equal(2, (await app.ChapterUseCases.ListAsync(manuscriptId)).Count);
    }

    [Fact]
    public async Task The_status_line_reports_where_the_file_went_and_what_it_carries()
    {
        // FR-019: the outcome is a sentence, not a spinner that stopped.
        await using var app = new AppHarness();
        Guid manuscriptId = await SeedAsync(app);
        IReadOnlyList<ChapterSummary> chapters = await app.ChapterUseCases.ListAsync(manuscriptId);

        await app.Images.AddAsync(
            new InlineImageInsert(chapters[0].Id, [0x89, 0x50, 0x4E, 0x47], "image/png", "The mill"),
            app.Clock.Now);

        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscriptId;
        app.Picker.NextSaveLocation = Path.Combine(app.ExportDirectory, "book.epub");

        await export.ExportManuscriptEpubCommand.ExecuteAsync(null);

        Assert.Contains("Saved to", export.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("1 image embedded", export.StatusMessage, StringComparison.Ordinal);
    }

    // ---- Navigation into export ----

    [Fact]
    public async Task The_library_can_send_a_manuscript_to_the_export_screen()
    {
        await using var app = new AppHarness();
        await SeedAsync(app);

        LibraryViewModel library = app.Library;
        await library.LoadAsync();
        await library.ExportCommand.ExecuteAsync(library.Manuscripts[0]);

        (string route, IDictionary<string, object>? parameters) = app.Navigation.Navigations[^1];
        Assert.Equal(Routes.Export, route);
        Assert.Equal("The Long Winter", parameters![Routes.ManuscriptTitleParameter]);
    }

    [Fact]
    public async Task The_editor_flushes_before_sending_the_writer_to_export()
    {
        // What is exported must be what is on screen, not the last debounced save.
        await using var app = new AppHarness(autoSaveDebounce: TimeSpan.FromMinutes(10));
        (_, Guid chapterId) = await app.OpenNewChapterAsync();

        app.EditorHost.Type(chapterId, "A sentence typed a moment ago.");
        await app.Editor.ExportCommand.ExecuteAsync(null);

        Assert.Equal("A sentence typed a moment ago.", await app.ReadStoredMarkdownAsync(chapterId));
        Assert.Equal(Routes.Export, app.Navigation.Navigations[^1].Route);
    }

    // ---- Data controls (FR-018, SC-008) ----

    [Fact]
    public async Task The_data_screen_lists_everything_the_app_holds()
    {
        await using var app = new AppHarness();
        await SeedAsync(app);

        DataControlsViewModel data = app.DataControls;
        await data.LoadAsync();

        ManuscriptDataInventory entry = Assert.Single(data.Manuscripts);
        Assert.Equal("The Long Winter", entry.Title);
        Assert.Equal(2, entry.ChapterCount);
        Assert.Equal(1, entry.CharacterCount);
        Assert.True(entry.HasDailyGoal);
        Assert.Equal("InkWell is storing 1 manuscript.", data.StatusMessage);
    }

    [Fact]
    public async Task The_data_screen_states_where_the_file_is_so_the_promise_is_checkable()
    {
        await using var app = new AppHarness();
        await SeedAsync(app);

        DataControlsViewModel data = app.DataControls;
        await data.LoadAsync();

        Assert.Contains("one encrypted file on this device", data.StorageSummary, StringComparison.Ordinal);
        Assert.Contains("Nothing is sent anywhere unless you export it", data.StorageSummary, StringComparison.Ordinal);
        Assert.True(File.Exists(data.DatabasePath));
    }

    [Fact]
    public async Task An_empty_app_says_so_rather_than_showing_a_blank_screen()
    {
        await using var app = new AppHarness();

        DataControlsViewModel data = app.DataControls;
        await data.LoadAsync();

        Assert.True(data.IsEmpty);
        Assert.Equal("InkWell is not storing any manuscripts.", data.StatusMessage);
    }

    [Fact]
    public async Task Deleting_a_manuscript_names_everything_that_will_be_lost_first()
    {
        // FR-005: informed consent means knowing the cost, not just being asked twice.
        await using var app = new AppHarness();
        await SeedAsync(app);

        DataControlsViewModel data = app.DataControls;
        await data.LoadAsync();

        app.Confirmation.NextAnswer = false;
        await data.DeleteManuscriptCommand.ExecuteAsync(data.Manuscripts[0]);

        (string title, string message) = Assert.Single(app.Confirmation.Prompts);
        Assert.Equal("Delete this manuscript?", title);
        Assert.Contains("The Long Winter", message, StringComparison.Ordinal);
        Assert.Contains("2 chapters", message, StringComparison.Ordinal);
        Assert.Contains("1 character", message, StringComparison.Ordinal);
        Assert.Contains("cannot be undone", message, StringComparison.Ordinal);

        Assert.Equal("Nothing was deleted.", data.StatusMessage);
        Assert.Single(data.Manuscripts);
    }

    [Fact]
    public async Task Confirming_removes_the_manuscript_and_leaves_the_others()
    {
        await using var app = new AppHarness();
        await SeedAsync(app, "The Long Winter");
        await SeedAsync(app, "A Second Novel");

        DataControlsViewModel data = app.DataControls;
        await data.LoadAsync();
        ManuscriptDataInventory doomed = data.Manuscripts.Single(m => m.Title == "The Long Winter");

        app.Confirmation.NextAnswer = true;
        await data.DeleteManuscriptCommand.ExecuteAsync(doomed);

        ManuscriptDataInventory survivor = Assert.Single(data.Manuscripts);
        Assert.Equal("A Second Novel", survivor.Title);
        Assert.Equal("Deleted “The Long Winter” and everything in it.", data.StatusMessage);
    }

    [Fact]
    public async Task Delete_everything_warns_that_the_key_goes_too()
    {
        // The detail that makes it unrecoverable even from a backup. Someone about to press it
        // deserves to know that before, not after.
        await using var app = new AppHarness();
        await SeedAsync(app);

        DataControlsViewModel data = app.DataControls;
        await data.LoadAsync();

        app.Confirmation.NextAnswer = false;
        await data.DeleteAllCommand.ExecuteAsync(null);

        (string title, string message) = Assert.Single(app.Confirmation.Prompts);
        Assert.Equal("Delete everything?", title);
        Assert.Contains("key that encrypts them", message, StringComparison.Ordinal);
        Assert.Contains("even from a backup", message, StringComparison.Ordinal);
        Assert.Contains("Export anything you want to keep first", message, StringComparison.Ordinal);
        Assert.Single(data.Manuscripts);
    }

    [Fact]
    public async Task Confirming_delete_everything_leaves_the_app_empty()
    {
        await using var app = new AppHarness();
        await SeedAsync(app, "The Long Winter");
        await SeedAsync(app, "A Second Novel");

        DataControlsViewModel data = app.DataControls;
        await data.LoadAsync();

        app.Confirmation.NextAnswer = true;
        await data.DeleteAllCommand.ExecuteAsync(null);

        Assert.Empty(data.Manuscripts);
        Assert.True(data.IsEmpty);
        Assert.Equal("Everything has been deleted. InkWell is now empty.", data.StatusMessage);
        Assert.Empty(await app.ManuscriptUseCases.ListAsync());
    }

    [Fact]
    public async Task The_writer_can_export_before_erasing_and_the_export_survives()
    {
        // The sequence the delete-everything dialog tells them to follow, end to end.
        await using var app = new AppHarness();
        Guid manuscriptId = await SeedAsync(app);

        string saved = Path.Combine(app.ExportDirectory, "keepsake.epub");
        ExportViewModel export = app.Export;
        export.ManuscriptId = manuscriptId;
        app.Picker.NextSaveLocation = saved;
        await export.ExportManuscriptEpubCommand.ExecuteAsync(null);

        DataControlsViewModel data = app.DataControls;
        await data.LoadAsync();
        app.Confirmation.NextAnswer = true;
        await data.DeleteAllCommand.ExecuteAsync(null);

        Assert.Empty(await app.ManuscriptUseCases.ListAsync());
        Assert.True(File.Exists(saved));
        Assert.True(new FileInfo(saved).Length > 0);
    }
}
