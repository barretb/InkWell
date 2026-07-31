using InkWell.Maui.UiTests.Harness;
using InkWell.Presentation.Services;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.UiTests;

/// <summary>
/// FR-004 applied to the whole app: nothing anywhere requires an explicit save.
/// </summary>
/// <remarks>
/// Chapters had this from the start; character notes, plot-thread notes, and the daily goal did
/// not — they shipped with per-item Save buttons, which made those three screens the only places
/// where forgetting to press something lost your work. These tests exist so that cannot come back.
/// </remarks>
public class AutoSaveEverywhereTests
{
    private static Debouncer Fast() => new(TimeSpan.FromMilliseconds(30));

    [Fact]
    public async Task Editing_a_characters_notes_saves_without_any_button()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        await using var characters = new CharactersViewModel(
            app.ReferenceUseCases, app.Confirmation, app.Errors, Fast());
        characters.ManuscriptId = manuscriptId;
        characters.NewName = "Elin";
        await characters.CreateAsync();

        // The writer simply types into the notes field.
        characters.Characters[0].Notes = "Left-handed. Afraid of the river.";
        await characters.FlushAsync();

        await using var reopened = new CharactersViewModel(
            app.ReferenceUseCases, app.Confirmation, app.Errors, Fast());
        reopened.ManuscriptId = manuscriptId;
        await reopened.LoadAsync();

        Assert.Equal("Left-handed. Afraid of the river.", reopened.Characters[0].Notes);
    }

    [Fact]
    public async Task A_character_edit_saves_on_its_own_after_a_pause()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        await using var characters = new CharactersViewModel(
            app.ReferenceUseCases, app.Confirmation, app.Errors, Fast());
        characters.ManuscriptId = manuscriptId;
        characters.NewName = "Elin";
        await characters.CreateAsync();

        characters.Characters[0].Name = "Elin Vasa";

        // No flush, no button — just a pause, exactly as if the writer stopped typing.
        await WaitForAsync(async () =>
            (await app.ReferenceUseCases.ListCharactersAsync(manuscriptId))[0].Name == "Elin Vasa");

        Assert.Equal("Elin Vasa", (await app.ReferenceUseCases.ListCharactersAsync(manuscriptId))[0].Name);
    }

    [Fact]
    public async Task Editing_a_plot_thread_saves_without_any_button()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        await using var threads = new PlotThreadsViewModel(
            app.ReferenceUseCases, app.Confirmation, app.Errors, Fast());
        threads.ManuscriptId = manuscriptId;
        threads.NewTitle = "The mill fire";
        await threads.CreateAsync();

        threads.PlotThreads[0].Notes = "Starts in ch. 3, pays off in ch. 12.";
        await threads.FlushAsync();

        Assert.Equal(
            "Starts in ch. 3, pays off in ch. 12.",
            (await app.ReferenceUseCases.ListPlotThreadsAsync(manuscriptId))[0].Notes);
    }

    [Fact]
    public async Task Typing_a_daily_goal_saves_without_any_button()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        await using var goals = new GoalsViewModel(app.GoalUseCases, app.Confirmation, app.Errors, Fast());
        goals.ManuscriptId = manuscriptId;
        await goals.LoadAsync();

        goals.TargetInput = "500";
        await goals.FlushAsync();

        Assert.Equal(500, (await app.GoalUseCases.GetTodayProgressAsync(manuscriptId)).Target);
    }

    [Fact]
    public async Task Half_typed_input_is_not_an_error_dialog()
    {
        // "5" on the way to "500" is not a mistake worth interrupting anyone for.
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        await using var goals = new GoalsViewModel(app.GoalUseCases, app.Confirmation, app.Errors, Fast());
        goals.ManuscriptId = manuscriptId;
        await goals.LoadAsync();

        goals.TargetInput = "abc";
        await goals.FlushAsync();

        Assert.Empty(app.Errors.Errors);
        Assert.Contains("Not saved", goals.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Loading_a_screen_does_not_count_as_the_writer_typing()
    {
        // Populating the field from the store must not immediately re-save it.
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();
        await app.GoalUseCases.SetGoalAsync(manuscriptId, 500);

        await using var goals = new GoalsViewModel(app.GoalUseCases, app.Confirmation, app.Errors, Fast());
        goals.ManuscriptId = manuscriptId;
        await goals.LoadAsync();

        Assert.DoesNotContain("Saving", goals.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("500", goals.TargetInput);
    }

    [Fact]
    public async Task An_empty_name_is_reported_quietly_and_not_saved()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        await using var characters = new CharactersViewModel(
            app.ReferenceUseCases, app.Confirmation, app.Errors, Fast());
        characters.ManuscriptId = manuscriptId;
        characters.NewName = "Elin";
        await characters.CreateAsync();

        characters.Characters[0].Name = "   ";
        await characters.FlushAsync();

        Assert.Empty(app.Errors.Errors);
        Assert.Contains("a name is required", characters.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Elin", (await app.ReferenceUseCases.ListCharactersAsync(manuscriptId))[0].Name);
    }

    [Fact]
    public async Task Disposing_a_screen_commits_whatever_was_still_pending()
    {
        // Closing the app or navigating away is not a decision to discard the last sentence.
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        var characters = new CharactersViewModel(
            app.ReferenceUseCases, app.Confirmation, app.Errors, new Debouncer(TimeSpan.FromMinutes(10)));
        characters.ManuscriptId = manuscriptId;
        characters.NewName = "Elin";
        await characters.CreateAsync();

        characters.Characters[0].Notes = "The very last thing typed.";
        await characters.DisposeAsync();

        Assert.Equal(
            "The very last thing typed.",
            (await app.ReferenceUseCases.ListCharactersAsync(manuscriptId))[0].Notes);
    }

    [Fact]
    public async Task Only_the_newest_edit_is_written()
    {
        await using var app = new AppHarness();
        (Guid manuscriptId, _) = await app.OpenNewChapterAsync();

        await using var threads = new PlotThreadsViewModel(
            app.ReferenceUseCases, app.Confirmation, app.Errors, new Debouncer(TimeSpan.FromMinutes(10)));
        threads.ManuscriptId = manuscriptId;
        threads.NewTitle = "Draft";
        await threads.CreateAsync();

        threads.PlotThreads[0].Title = "The m";
        threads.PlotThreads[0].Title = "The mill";
        threads.PlotThreads[0].Title = "The mill fire";
        await threads.FlushAsync();

        Assert.Equal("The mill fire", (await app.ReferenceUseCases.ListPlotThreadsAsync(manuscriptId))[0].Title);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20);
        }
    }
}
