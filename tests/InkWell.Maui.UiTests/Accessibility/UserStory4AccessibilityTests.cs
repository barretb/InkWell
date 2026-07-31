using InkWell.Maui.UiTests.Harness;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.UiTests.Accessibility;

/// <summary>
/// US4 · FR-019, SC-007 — the reference lists are completable by keyboard alone, every outcome is
/// announced in words, and the empty states explain rather than show nothing.
/// </summary>
public class UserStory4AccessibilityTests
{
    [Fact]
    public async Task The_whole_character_journey_is_completable_without_a_pointer()
    {
        // Every step below is a command bound to a control that a keyboard can reach; none depends
        // on a gesture, a drag, or a hover.
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        CharactersViewModel characters = app.Characters;
        characters.ManuscriptId = manuscriptId;

        characters.NewName = "Elin";
        characters.NewNotes = "Miller's daughter.";
        await characters.CreateCommand.ExecuteAsync(null);
        Assert.Single(characters.Characters);

        characters.Characters[0].Notes = "Miller's daughter. Left-handed.";
        await characters.FlushAsync();
        Assert.Equal("Miller's daughter. Left-handed.", characters.Characters[0].Notes);

        app.Confirmation.NextAnswer = true;
        await characters.DeleteCommand.ExecuteAsync(characters.Characters[0]);
        Assert.Empty(characters.Characters);
    }

    [Fact]
    public async Task Every_outcome_is_stated_in_words()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();
        CharactersViewModel characters = app.Characters;
        characters.ManuscriptId = manuscriptId;

        await characters.LoadAsync();
        Assert.Equal("No characters yet.", characters.StatusMessage);

        characters.NewName = "Elin";
        await characters.CreateAsync();
        Assert.Equal("Added Elin.", characters.StatusMessage);

        app.Confirmation.NextAnswer = false;
        await characters.DeleteAsync(characters.Characters[0]);
        Assert.Equal("Nothing was deleted.", characters.StatusMessage);

        app.Confirmation.NextAnswer = true;
        await characters.DeleteAsync(characters.Characters[0]);
        Assert.Equal("Deleted Elin.", characters.StatusMessage);
    }

    [Fact]
    public async Task The_counts_are_announced_with_correct_grammar()
    {
        // A screen reader saying "1 characters" is the kind of thing that reads as carelessness.
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();
        PlotThreadsViewModel threads = app.PlotThreads;
        threads.ManuscriptId = manuscriptId;

        threads.NewTitle = "The mill fire";
        await threads.CreateAsync();
        await threads.LoadAsync();
        Assert.Equal("1 plot thread.", threads.StatusMessage);

        threads.NewTitle = "The missing ledger";
        await threads.CreateAsync();
        await threads.LoadAsync();
        Assert.Equal("2 plot threads.", threads.StatusMessage);
    }

    [Fact]
    public async Task Empty_reference_lists_report_themselves_as_empty()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        CharactersViewModel characters = app.Characters;
        characters.ManuscriptId = manuscriptId;
        await characters.LoadAsync();

        PlotThreadsViewModel threads = app.PlotThreads;
        threads.ManuscriptId = manuscriptId;
        await threads.LoadAsync();

        Assert.True(characters.IsEmpty);
        Assert.True(threads.IsEmpty);
    }

    [Fact]
    public async Task Returning_from_a_reference_announces_that_writing_resumed()
    {
        // A screen-reader user needs to hear that focus went back to the chapter, not guess.
        await using var app = new AppHarness();
        (Guid _, Guid chapterId) = await app.OpenNewChapterAsync();
        app.EditorHost.Type(chapterId, "Elin watched the mill burn.");

        await app.Editor.OpenCharactersAsync();
        await app.Editor.ResumeWritingAsync();

        Assert.Equal("Back to your chapter.", app.Editor.StatusMessage);
    }
}
