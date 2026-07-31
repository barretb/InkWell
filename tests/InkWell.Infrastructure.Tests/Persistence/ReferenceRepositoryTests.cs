using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Tests.Fixtures;

namespace InkWell.Infrastructure.Tests.Persistence;

/// <summary>
/// US4 · contracts/reference-service.md — characters and plot threads round-trip, edits persist,
/// and deleting one removes only that entry.
/// </summary>
public class ReferenceRepositoryTests
{
    [Fact]
    public async Task A_created_character_is_listed_for_its_manuscript()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);

        DomainResult<Character> created = await fixture.ReferenceUseCases
            .CreateCharacterAsync(manuscript.Id, "Elin", "The miller's daughter. Left-handed.");

        Assert.True(created.IsSuccess);
        Character only = Assert.Single(await fixture.ReferenceUseCases.ListCharactersAsync(manuscript.Id));
        Assert.Equal("Elin", only.Name);
        Assert.Equal("The miller's daughter. Left-handed.", only.Notes);
    }

    [Fact]
    public async Task A_created_plot_thread_is_listed_for_its_manuscript()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);

        await fixture.ReferenceUseCases.CreatePlotThreadAsync(manuscript.Id, "The mill fire", "Pays off in ch. 12.");

        PlotThread only = Assert.Single(await fixture.ReferenceUseCases.ListPlotThreadsAsync(manuscript.Id));
        Assert.Equal("The mill fire", only.Title);
    }

    [Fact]
    public async Task Both_kinds_of_reference_survive_a_restart()
    {
        // The US4 independent test.
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        await fixture.ReferenceUseCases.CreateCharacterAsync(manuscript.Id, "Elin", "Miller's daughter.");
        await fixture.ReferenceUseCases.CreatePlotThreadAsync(manuscript.Id, "The mill fire", "Ch. 12.");

        await fixture.RestartAsync();

        Assert.Equal("Elin", Assert.Single(await fixture.ReferenceUseCases.ListCharactersAsync(manuscript.Id)).Name);
        Assert.Equal("The mill fire", Assert.Single(await fixture.ReferenceUseCases.ListPlotThreadsAsync(manuscript.Id)).Title);
    }

    [Fact]
    public async Task Notes_may_be_empty()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);

        DomainResult<Character> created = await fixture.ReferenceUseCases
            .CreateCharacterAsync(manuscript.Id, "Someone", null);

        Assert.True(created.IsSuccess);
        Assert.Equal(string.Empty, created.Value.Notes);
    }

    [Fact]
    public async Task A_name_is_required()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);

        DomainResult<Character> created = await fixture.ReferenceUseCases
            .CreateCharacterAsync(manuscript.Id, "   ", "notes");

        Assert.Equal(DomainErrorCode.ValidationError, created.Error.Code);
        Assert.Empty(await fixture.ReferenceUseCases.ListCharactersAsync(manuscript.Id));
    }

    [Fact]
    public async Task Characters_are_listed_alphabetically_regardless_of_case()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        await fixture.ReferenceUseCases.CreateCharacterAsync(manuscript.Id, "elin", null);
        await fixture.ReferenceUseCases.CreateCharacterAsync(manuscript.Id, "Bram", null);
        await fixture.ReferenceUseCases.CreateCharacterAsync(manuscript.Id, "Astrid", null);

        IReadOnlyList<Character> characters = await fixture.ReferenceUseCases.ListCharactersAsync(manuscript.Id);

        Assert.Equal(["Astrid", "Bram", "elin"], characters.Select(c => c.Name));
    }

    [Fact]
    public async Task An_edit_persists()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        Character character = (await fixture.ReferenceUseCases
            .CreateCharacterAsync(manuscript.Id, "Elin", "Miller's daughter.")).Value;

        DomainResult updated = await fixture.ReferenceUseCases
            .UpdateCharacterAsync(character.Id, "Elin Vasa", "Miller's daughter. Left-handed.");

        Assert.True(updated.IsSuccess);
        await fixture.RestartAsync();
        Character reloaded = Assert.Single(await fixture.ReferenceUseCases.ListCharactersAsync(manuscript.Id));
        Assert.Equal("Elin Vasa", reloaded.Name);
        Assert.Equal("Miller's daughter. Left-handed.", reloaded.Notes);
    }

    [Fact]
    public async Task Editing_something_that_is_gone_reports_not_found()
    {
        await using var fixture = new StoreFixture();
        await SeedAsync(fixture);

        Assert.Equal(
            DomainErrorCode.NotFound,
            (await fixture.ReferenceUseCases.UpdatePlotThreadAsync(Guid.NewGuid(), "Anything", null)).Error.Code);
    }

    [Fact]
    public async Task Deleting_a_character_named_in_the_prose_leaves_the_manuscript_untouched()
    {
        // The spec's "deleting an in-use reference" edge case.
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        Character character = (await fixture.ReferenceUseCases
            .CreateCharacterAsync(manuscript.Id, "Elin", "Miller's daughter.")).Value;

        const string prose = "Elin watched the mill burn from the ridge.";
        await fixture.WriteWordsAsync(manuscript.Id, 8);
        Guid chapterId = (await fixture.ChapterUseCases.ListAsync(manuscript.Id))[0].Id;
        await fixture.Chapters.CommitAutoSaveAsync(new Application.Abstractions.Dtos.AutoSaveCommit(
            chapterId, prose, 8, fixture.Clock.Now, fixture.Clock.Today));

        await fixture.ReferenceUseCases.DeleteCharacterAsync(character.Id);

        Assert.Empty(await fixture.ReferenceUseCases.ListCharactersAsync(manuscript.Id));
        Assert.Equal(prose, (await fixture.ChapterUseCases.GetContentAsync(chapterId)).Value.ContentMarkdown);
    }

    [Fact]
    public async Task Deleting_one_reference_leaves_the_others()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        Character first = (await fixture.ReferenceUseCases.CreateCharacterAsync(manuscript.Id, "Elin", null)).Value;
        await fixture.ReferenceUseCases.CreateCharacterAsync(manuscript.Id, "Bram", null);
        await fixture.ReferenceUseCases.CreatePlotThreadAsync(manuscript.Id, "The mill fire", null);

        await fixture.ReferenceUseCases.DeleteCharacterAsync(first.Id);

        Assert.Equal("Bram", Assert.Single(await fixture.ReferenceUseCases.ListCharactersAsync(manuscript.Id)).Name);
        Assert.Single(await fixture.ReferenceUseCases.ListPlotThreadsAsync(manuscript.Id));
    }

    [Fact]
    public async Task References_belong_to_one_manuscript_only()
    {
        await using var fixture = new StoreFixture();
        Manuscript first = await SeedAsync(fixture, "First");
        Manuscript second = await SeedAsync(fixture, "Second");
        await fixture.ReferenceUseCases.CreateCharacterAsync(first.Id, "Elin", null);

        Assert.Empty(await fixture.ReferenceUseCases.ListCharactersAsync(second.Id));
    }

    [Fact]
    public async Task Deleting_a_manuscript_takes_its_references_with_it()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = await SeedAsync(fixture);
        await fixture.ReferenceUseCases.CreateCharacterAsync(manuscript.Id, "Elin", null);
        await fixture.ReferenceUseCases.CreatePlotThreadAsync(manuscript.Id, "The mill fire", null);

        await fixture.ManuscriptUseCases.DeleteAsync(manuscript.Id);

        Assert.Empty(await fixture.ReferenceUseCases.ListCharactersAsync(manuscript.Id));
        Assert.Empty(await fixture.ReferenceUseCases.ListPlotThreadsAsync(manuscript.Id));
    }

    [Fact]
    public async Task Adding_a_reference_to_a_manuscript_that_is_gone_reports_not_found()
    {
        await using var fixture = new StoreFixture();

        Assert.Equal(
            DomainErrorCode.NotFound,
            (await fixture.ReferenceUseCases.CreateCharacterAsync(Guid.NewGuid(), "Elin", null)).Error.Code);
    }

    private static async Task<Manuscript> SeedAsync(StoreFixture fixture, string title = "The Long Winter")
    {
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync(title)).Value;
        await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One");
        return manuscript;
    }
}
