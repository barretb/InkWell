using InkWell.Application.Tests.Fakes;
using InkWell.Application.UseCases;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;

namespace InkWell.Application.Tests.UseCases;

/// <summary>
/// T098 — character and plot-thread orchestration, over fakes (FR-013, FR-014, FR-015).
/// </summary>
/// <remarks>
/// Characters and plot threads share one rule set, so each behaviour is asserted for both types
/// rather than trusting that the second copy of the code kept in step with the first.
/// </remarks>
public class ReferenceUseCasesTests
{
    private readonly FakeReferenceRepository _references = new();
    private readonly FakeManuscriptRepository _manuscripts = new();
    private readonly FixedClock _clock = new();
    private readonly ReferenceUseCases _sut;
    private readonly Manuscript _manuscript;

    public ReferenceUseCasesTests()
    {
        _sut = new ReferenceUseCases(_references, _manuscripts, _clock);
        _manuscript = _manuscripts.Seed("The Long Winter");
    }

    // ---- Name and title validation ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_character_without_a_name_is_a_validation_error(string? name)
    {
        DomainResult<Character> result = await _sut.CreateCharacterAsync(_manuscript.Id, name, "notes");

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
        Assert.Empty(await _sut.ListCharactersAsync(_manuscript.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_plot_thread_without_a_title_is_a_validation_error(string? title)
    {
        DomainResult<PlotThread> result = await _sut.CreatePlotThreadAsync(_manuscript.Id, title, "notes");

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
        Assert.Empty(await _sut.ListPlotThreadsAsync(_manuscript.Id));
    }

    [Fact]
    public async Task A_character_name_over_two_hundred_characters_is_rejected()
    {
        DomainResult<Character> result =
            await _sut.CreateCharacterAsync(_manuscript.Id, new string('e', EntityTitle.MaxLength + 1), null);

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
    }

    [Fact]
    public async Task A_plot_thread_title_over_two_hundred_characters_is_rejected()
    {
        DomainResult<PlotThread> result =
            await _sut.CreatePlotThreadAsync(_manuscript.Id, new string('t', EntityTitle.MaxLength + 1), null);

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
    }

    [Fact]
    public async Task Names_and_titles_are_stored_trimmed()
    {
        Character character = (await _sut.CreateCharacterAsync(_manuscript.Id, "  Elin  ", null)).Value;
        PlotThread thread = (await _sut.CreatePlotThreadAsync(_manuscript.Id, "\tThe Mill Fire\n", null)).Value;

        Assert.Equal("Elin", character.Name);
        Assert.Equal("The Mill Fire", thread.Title);
    }

    // ---- Freeform notes ----

    [Fact]
    public async Task Notes_are_freeform_and_absent_notes_become_empty_rather_than_null()
    {
        Character character = (await _sut.CreateCharacterAsync(_manuscript.Id, "Elin", null)).Value;
        PlotThread thread = (await _sut.CreatePlotThreadAsync(_manuscript.Id, "The Mill Fire", null)).Value;

        Assert.Equal(string.Empty, character.Notes);
        Assert.Equal(string.Empty, thread.Notes);
    }

    [Fact]
    public async Task Notes_keep_whatever_the_writer_typed_including_newlines_and_markdown()
    {
        const string notes = "  Miller's daughter.\n\n- Left-handed\n- *Lies* about the fire  ";

        Character character = (await _sut.CreateCharacterAsync(_manuscript.Id, "Elin", notes)).Value;

        Assert.Equal(notes, character.Notes);
        Assert.Equal(notes, _references.PeekCharacter(character.Id)!.Notes);
    }

    [Fact]
    public async Task Notes_may_be_long_where_a_name_may_not()
    {
        string essay = new('n', EntityTitle.MaxLength * 10);

        DomainResult<Character> result = await _sut.CreateCharacterAsync(_manuscript.Id, "Elin", essay);

        Assert.True(result.IsSuccess);
        Assert.Equal(essay.Length, result.Value.Notes.Length);
    }

    // ---- Manuscript scoping ----

    [Fact]
    public async Task Creating_a_character_on_a_manuscript_that_is_gone_reports_not_found()
    {
        DomainResult<Character> result = await _sut.CreateCharacterAsync(Guid.NewGuid(), "Elin", null);

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task Creating_a_plot_thread_on_a_manuscript_that_is_gone_reports_not_found()
    {
        DomainResult<PlotThread> result = await _sut.CreatePlotThreadAsync(Guid.NewGuid(), "The Mill Fire", null);

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task References_belong_to_their_own_manuscript_and_no_other()
    {
        Manuscript other = _manuscripts.Seed("A Different Novel");
        await _sut.CreateCharacterAsync(_manuscript.Id, "Elin", null);
        await _sut.CreateCharacterAsync(other.Id, "Tomas", null);

        IReadOnlyList<Character> mine = await _sut.ListCharactersAsync(_manuscript.Id);

        Assert.Equal(["Elin"], mine.Select(c => c.Name));
    }

    // ---- Listing order (contracts/reference-service.md) ----

    [Fact]
    public async Task Characters_are_listed_by_name()
    {
        await _sut.CreateCharacterAsync(_manuscript.Id, "Tomas", null);
        await _sut.CreateCharacterAsync(_manuscript.Id, "Elin", null);
        await _sut.CreateCharacterAsync(_manuscript.Id, "Marek", null);

        IReadOnlyList<Character> characters = await _sut.ListCharactersAsync(_manuscript.Id);

        Assert.Equal(["Elin", "Marek", "Tomas"], characters.Select(c => c.Name));
    }

    [Fact]
    public async Task Plot_threads_are_listed_by_title()
    {
        await _sut.CreatePlotThreadAsync(_manuscript.Id, "The Mill Fire", null);
        await _sut.CreatePlotThreadAsync(_manuscript.Id, "Elin's Secret", null);

        IReadOnlyList<PlotThread> threads = await _sut.ListPlotThreadsAsync(_manuscript.Id);

        Assert.Equal(["Elin's Secret", "The Mill Fire"], threads.Select(t => t.Title));
    }

    [Fact]
    public async Task A_manuscript_with_no_references_lists_empty_rather_than_failing()
    {
        Assert.Empty(await _sut.ListCharactersAsync(_manuscript.Id));
        Assert.Empty(await _sut.ListPlotThreadsAsync(_manuscript.Id));
    }

    // ---- Update ----

    [Fact]
    public async Task Updating_a_character_persists_both_fields_and_stamps_the_clock()
    {
        Character character = (await _sut.CreateCharacterAsync(_manuscript.Id, "Elin", "first note")).Value;
        _clock.Advance(TimeSpan.FromHours(5));

        DomainResult result = await _sut.UpdateCharacterAsync(character.Id, "Elin Vasa", "second note");

        Assert.True(result.IsSuccess);
        Character stored = _references.PeekCharacter(character.Id)!;
        Assert.Equal("Elin Vasa", stored.Name);
        Assert.Equal("second note", stored.Notes);
        Assert.Equal(_clock.Now, stored.ModifiedAt);
        Assert.Equal(character.CreatedAt, stored.CreatedAt);
    }

    [Fact]
    public async Task Updating_a_plot_thread_persists_both_fields_and_stamps_the_clock()
    {
        PlotThread thread = (await _sut.CreatePlotThreadAsync(_manuscript.Id, "The Fire", "first note")).Value;
        _clock.Advance(TimeSpan.FromHours(5));

        DomainResult result = await _sut.UpdatePlotThreadAsync(thread.Id, "The Mill Fire", "second note");

        Assert.True(result.IsSuccess);
        PlotThread stored = _references.PeekPlotThread(thread.Id)!;
        Assert.Equal("The Mill Fire", stored.Title);
        Assert.Equal("second note", stored.Notes);
        Assert.Equal(_clock.Now, stored.ModifiedAt);
    }

    [Fact]
    public async Task Clearing_notes_is_allowed_where_clearing_a_name_is_not()
    {
        Character character = (await _sut.CreateCharacterAsync(_manuscript.Id, "Elin", "a note")).Value;

        DomainResult cleared = await _sut.UpdateCharacterAsync(character.Id, "Elin", null);
        DomainResult unnamed = await _sut.UpdateCharacterAsync(character.Id, "  ", "a note");

        Assert.True(cleared.IsSuccess);
        Assert.Equal(string.Empty, _references.PeekCharacter(character.Id)!.Notes);
        Assert.True(unnamed.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, unnamed.Error.Code);
        Assert.Equal("Elin", _references.PeekCharacter(character.Id)!.Name);
    }

    [Fact]
    public async Task Updating_a_character_that_is_gone_reports_not_found()
    {
        DomainResult result = await _sut.UpdateCharacterAsync(Guid.NewGuid(), "Elin", null);

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task Updating_a_plot_thread_that_is_gone_reports_not_found()
    {
        DomainResult result = await _sut.UpdatePlotThreadAsync(Guid.NewGuid(), "The Mill Fire", null);

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    // ---- Delete (FR-005; the edge case in spec.md) ----

    [Fact]
    public async Task Deleting_a_character_removes_only_that_entry()
    {
        Character elin = (await _sut.CreateCharacterAsync(_manuscript.Id, "Elin", null)).Value;
        await _sut.CreateCharacterAsync(_manuscript.Id, "Tomas", null);
        PlotThread thread = (await _sut.CreatePlotThreadAsync(_manuscript.Id, "The Mill Fire", "Elin sets it")).Value;

        DomainResult result = await _sut.DeleteCharacterAsync(elin.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Tomas"], (await _sut.ListCharactersAsync(_manuscript.Id)).Select(c => c.Name));

        // The plot thread names the deleted character in its notes and is completely unaffected.
        Assert.Equal("Elin sets it", _references.PeekPlotThread(thread.Id)!.Notes);
        Assert.NotNull(_manuscripts.Peek(_manuscript.Id));
    }

    [Fact]
    public async Task Deleting_a_plot_thread_removes_only_that_entry()
    {
        PlotThread fire = (await _sut.CreatePlotThreadAsync(_manuscript.Id, "The Mill Fire", null)).Value;
        await _sut.CreatePlotThreadAsync(_manuscript.Id, "Elin's Secret", null);
        Character character = (await _sut.CreateCharacterAsync(_manuscript.Id, "Elin", "Burns the mill")).Value;

        DomainResult result = await _sut.DeletePlotThreadAsync(fire.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Elin's Secret"], (await _sut.ListPlotThreadsAsync(_manuscript.Id)).Select(t => t.Title));
        Assert.Equal("Burns the mill", _references.PeekCharacter(character.Id)!.Notes);
    }

    [Fact]
    public async Task Deleting_a_character_that_is_gone_reports_not_found()
    {
        DomainResult result = await _sut.DeleteCharacterAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task Deleting_a_plot_thread_that_is_gone_reports_not_found()
    {
        DomainResult result = await _sut.DeletePlotThreadAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }
}
