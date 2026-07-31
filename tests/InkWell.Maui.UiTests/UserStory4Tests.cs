using InkWell.Domain.Entities;
using InkWell.Maui.UiTests.Harness;
using InkWell.Presentation;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.UiTests;

/// <summary>
/// User Story 4 end to end: "Create a character with notes and a plot thread with notes, associate
/// them with a manuscript, close and reopen the app, and confirm both are retained and viewable
/// alongside the manuscript."
/// </summary>
public class UserStory4Tests
{
    [Fact]
    public async Task The_writer_records_a_character_and_a_plot_thread_and_finds_them_after_a_restart()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        // 1. Create a character with notes (US4 scenario 1).
        CharactersViewModel characters = app.Characters;
        characters.ManuscriptId = manuscriptId;
        characters.NewName = "Elin Vasa";
        characters.NewNotes = "The miller's daughter. Left-handed. Afraid of the river.";
        await characters.CreateAsync();

        Assert.Equal("Elin Vasa", Assert.Single(characters.Characters).Name);

        // 2. Create a plot thread with notes (US4 scenario 2).
        PlotThreadsViewModel threads = app.PlotThreads;
        threads.ManuscriptId = manuscriptId;
        threads.NewTitle = "The mill fire";
        threads.NewNotes = "Set in chapter 3, pays off in chapter 12.";
        await threads.CreateAsync();

        Assert.Equal("The mill fire", Assert.Single(threads.PlotThreads).Title);

        // 3. Close and reopen the app; both are retained (US4 scenario 5).
        await app.RestartAsync();

        CharactersViewModel reloadedCharacters = app.Characters;
        reloadedCharacters.ManuscriptId = manuscriptId;
        await reloadedCharacters.LoadAsync();

        PlotThreadsViewModel reloadedThreads = app.PlotThreads;
        reloadedThreads.ManuscriptId = manuscriptId;
        await reloadedThreads.LoadAsync();

        CharacterEditor character = Assert.Single(reloadedCharacters.Characters);
        Assert.Equal("Elin Vasa", character.Name);
        Assert.Equal("The miller's daughter. Left-handed. Afraid of the river.", character.Notes);
        Assert.Equal("Set in chapter 3, pays off in chapter 12.", Assert.Single(reloadedThreads.PlotThreads).Notes);
    }

    [Fact]
    public async Task Opening_a_reference_while_drafting_does_not_lose_the_writers_place()
    {
        // US4 scenario 4 / FR-015 — the requirement that makes references usable mid-sentence.
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();
        app.EditorHost.Type(chapterId, "Elin watched the mill burn from the ridge.");
        app.EditorHost.PlaceCaret(4);

        await app.Editor.OpenCharactersAsync();

        // Leaving flushes, so nothing typed is at risk while the writer is looking something up.
        Assert.Equal(
            "Elin watched the mill burn from the ridge.",
            await app.ReadStoredMarkdownAsync(chapterId));
        Assert.Equal(Routes.Characters, app.Navigation.Navigations[^1].Route);
        Assert.Equal(manuscriptId, app.Navigation.Navigations[^1].Parameters![Routes.ManuscriptIdParameter]);

        // Coming back restores focus and the caret; the document was never touched.
        int documentReplacementsBefore = app.EditorHost.DocumentReplacements;
        await app.Editor.ResumeWritingAsync();

        Assert.Equal(4, app.EditorHost.Caret);
        Assert.Equal("Elin watched the mill burn from the ridge.", app.EditorHost.Document);
        Assert.Equal(documentReplacementsBefore, app.EditorHost.DocumentReplacements);
        Assert.True(app.EditorHost.FocusRestoreCount > 0);
    }

    [Fact]
    public async Task Plot_threads_open_from_the_editor_the_same_way()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        await app.Editor.OpenPlotThreadsAsync();

        Assert.Equal(Routes.PlotThreads, app.Navigation.Navigations[^1].Route);
        Assert.Equal(manuscriptId, app.Navigation.Navigations[^1].Parameters![Routes.ManuscriptIdParameter]);
    }

    [Fact]
    public async Task An_edit_persists_and_a_delete_needs_confirmation()
    {
        // US4 scenario 3.
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        CharactersViewModel characters = app.Characters;
        characters.ManuscriptId = manuscriptId;
        characters.NewName = "Elin";
        await characters.CreateAsync();
        characters.NewName = "Bram";
        await characters.CreateAsync();

        // No save button: typing into the notes field is the whole interaction (FR-004).
        CharacterEditor elin = characters.Characters.Single(c => c.Name == "Elin");
        elin.Notes = "Left-handed.";
        await characters.FlushAsync();

        Assert.Equal(
            "Left-handed.",
            (await app.ReferenceUseCases.ListCharactersAsync(manuscriptId)).Single(c => c.Name == "Elin").Notes);

        // Declining leaves everything alone.
        app.Confirmation.NextAnswer = false;
        await characters.DeleteAsync(characters.Characters.Single(c => c.Name == "Bram"));
        Assert.Equal(2, characters.Characters.Count);

        // Confirming removes only that one.
        app.Confirmation.NextAnswer = true;
        await characters.DeleteAsync(characters.Characters.Single(c => c.Name == "Bram"));
        Assert.Equal("Elin", Assert.Single(characters.Characters).Name);
    }

    [Fact]
    public async Task Deleting_a_character_never_touches_the_manuscript()
    {
        // The spec's "deleting an in-use reference" edge case, from the writer's seat.
        await using var app = new AppHarness();
        (Guid manuscriptId, Guid chapterId) = await app.OpenNewChapterAsync();
        app.EditorHost.Type(chapterId, "Elin watched the mill burn from the ridge.");
        await app.Editor.FlushAsync();

        CharactersViewModel characters = app.Characters;
        characters.ManuscriptId = manuscriptId;
        characters.NewName = "Elin";
        await characters.CreateAsync();

        app.Confirmation.NextAnswer = true;
        await characters.DeleteAsync(characters.Characters[0]);

        Assert.Empty(characters.Characters);
        Assert.Equal(
            "Elin watched the mill burn from the ridge.",
            await app.ReadStoredMarkdownAsync(chapterId));
    }

    [Fact]
    public async Task The_deletion_prompt_says_the_manuscript_is_safe()
    {
        // Informed consent: a writer should not have to guess whether deleting a character will
        // damage prose that mentions them.
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();
        CharactersViewModel characters = app.Characters;
        characters.ManuscriptId = manuscriptId;
        characters.NewName = "Elin";
        await characters.CreateAsync();

        app.Confirmation.NextAnswer = false;
        await characters.DeleteAsync(characters.Characters[0]);

        Assert.Contains(
            "chapters are not changed",
            app.Confirmation.Prompts[^1].Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_reference_without_a_name_is_refused_with_an_explanation()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        PlotThreadsViewModel threads = app.PlotThreads;
        threads.ManuscriptId = manuscriptId;
        threads.NewTitle = "   ";
        threads.NewNotes = "notes without a title";
        await threads.CreateAsync();

        Assert.Single(app.Errors.Errors);
        Assert.Empty(threads.PlotThreads);
    }
}
