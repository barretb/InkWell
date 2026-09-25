using InkWell.Application.Abstractions.Dtos;
using InkWell.Application.Tests.Fakes;
using InkWell.Application.UseCases;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;
using InkWell.Domain.Services;

namespace InkWell.Application.Tests.UseCases;

/// <summary>
/// T050 — manuscript and chapter orchestration, over fakes.
/// </summary>
/// <remarks>
/// These assert the decisions the application layer makes before it reaches storage: what it
/// validates, which clock reading it stamps a write with, and whether it touches the manuscript so
/// the library's newest-first ordering stays truthful. The integration suite proves the same
/// journeys end to end against a real encrypted database; it cannot see which timestamp was passed
/// or whether a second write happened, and that is what is checked here.
/// </remarks>
public class ManuscriptUseCasesTests
{
    private readonly FakeManuscriptRepository _manuscripts = new();
    private readonly FakeChapterRepository _chapters = new();
    private readonly FixedClock _clock = new();
    private readonly ManuscriptUseCases _sut;
    private readonly ChapterUseCases _chapterUseCases;

    public ManuscriptUseCasesTests()
    {
        _manuscripts.Chapters = _chapters;
        _sut = new ManuscriptUseCases(_manuscripts, _clock);
        _chapterUseCases = new ChapterUseCases(_chapters, _manuscripts, _clock);
    }

    // ---- Title validation (data-model.md: required, trimmed, 1–200) ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public async Task Creating_a_manuscript_without_a_title_is_a_validation_error(string? title)
    {
        DomainResult<Manuscript> result = await _sut.CreateAsync(title);

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
        Assert.Equal(0, _manuscripts.Count);
    }

    [Fact]
    public async Task A_title_longer_than_two_hundred_characters_is_rejected()
    {
        DomainResult<Manuscript> result = await _sut.CreateAsync(new string('a', EntityTitle.MaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
        Assert.Equal(0, _manuscripts.Count);
    }

    [Fact]
    public async Task A_title_of_exactly_two_hundred_characters_is_accepted()
    {
        DomainResult<Manuscript> result = await _sut.CreateAsync(new string('a', EntityTitle.MaxLength));

        Assert.True(result.IsSuccess);
        Assert.Equal(EntityTitle.MaxLength, result.Value.Title.Length);
    }

    [Fact]
    public async Task A_title_is_stored_trimmed()
    {
        DomainResult<Manuscript> result = await _sut.CreateAsync("  The Long Winter\t");

        Assert.True(result.IsSuccess);
        Assert.Equal("The Long Winter", result.Value.Title);
        Assert.Equal("The Long Winter", _manuscripts.Peek(result.Value.Id)!.Title);
    }

    [Fact]
    public async Task A_new_manuscript_is_created_and_modified_at_the_same_instant()
    {
        DomainResult<Manuscript> result = await _sut.CreateAsync("The Long Winter");

        Assert.True(result.IsSuccess);
        Assert.Equal(_clock.Now, result.Value.CreatedAt);
        Assert.Equal(result.Value.CreatedAt, result.Value.ModifiedAt);
    }

    // ---- Rename ----

    [Fact]
    public async Task Renaming_stamps_the_manuscript_with_the_current_clock_reading()
    {
        Manuscript manuscript = _manuscripts.Seed("Working Title");
        _clock.Advance(TimeSpan.FromHours(3));

        DomainResult result = await _sut.RenameAsync(manuscript.Id, "The Long Winter");

        Assert.True(result.IsSuccess);
        Manuscript stored = _manuscripts.Peek(manuscript.Id)!;
        Assert.Equal("The Long Winter", stored.Title);
        Assert.Equal(_clock.Now, stored.ModifiedAt);
        Assert.NotEqual(stored.CreatedAt, stored.ModifiedAt);
    }

    [Fact]
    public async Task Renaming_a_manuscript_that_is_gone_reports_not_found()
    {
        DomainResult result = await _sut.RenameAsync(Guid.NewGuid(), "Anything");

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task An_invalid_new_title_is_rejected_before_the_store_is_touched()
    {
        Manuscript manuscript = _manuscripts.Seed("Working Title");

        DomainResult result = await _sut.RenameAsync(manuscript.Id, "   ");

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
        Assert.Equal("Working Title", _manuscripts.Peek(manuscript.Id)!.Title);
    }

    // ---- Delete ----

    [Fact]
    public async Task Deleting_a_manuscript_that_is_gone_reports_not_found()
    {
        DomainResult result = await _sut.DeleteAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task Deleting_takes_the_manuscript_and_its_chapters_with_it()
    {
        Manuscript manuscript = _manuscripts.Seed();
        await _chapterUseCases.AddAsync(manuscript.Id, "One");
        await _chapterUseCases.AddAsync(manuscript.Id, "Two");

        DomainResult result = await _sut.DeleteAsync(manuscript.Id);

        Assert.True(result.IsSuccess);
        Assert.Null(_manuscripts.Peek(manuscript.Id));
        Assert.Equal(0, _chapters.CountFor(manuscript.Id));
    }

    // ---- Library listing ----

    [Fact]
    public async Task The_library_lists_newest_modified_first()
    {
        Manuscript older = _manuscripts.Seed("Older", DateTimeOffset.UnixEpoch);
        Manuscript newer = _manuscripts.Seed("Newer", DateTimeOffset.UnixEpoch.AddDays(1));

        IReadOnlyList<ManuscriptSummary> library = await _sut.ListAsync();

        Assert.Equal([newer.Id, older.Id], library.Select(m => m.Id));
    }

    [Fact]
    public async Task An_empty_library_is_an_empty_list_rather_than_a_failure()
    {
        IReadOnlyList<ManuscriptSummary> library = await _sut.ListAsync();

        Assert.Empty(library);
    }

    [Fact]
    public async Task Opening_a_manuscript_that_is_gone_reports_not_found()
    {
        DomainResult<ManuscriptDetail> result = await _sut.GetAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    // ---- Chapters: add ----

    [Fact]
    public async Task Adding_a_chapter_appends_it_after_the_last_one()
    {
        Manuscript manuscript = _manuscripts.Seed();

        Chapter first = (await _chapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        Chapter second = (await _chapterUseCases.AddAsync(manuscript.Id, "Two")).Value;
        Chapter third = (await _chapterUseCases.AddAsync(manuscript.Id, "Three")).Value;

        Assert.Equal(0, first.OrderIndex);
        Assert.Equal(1, second.OrderIndex);
        Assert.Equal(2, third.OrderIndex);
    }

    [Fact]
    public async Task A_new_chapter_starts_empty_and_uncounted()
    {
        Manuscript manuscript = _manuscripts.Seed();

        Chapter chapter = (await _chapterUseCases.AddAsync(manuscript.Id, "One")).Value;

        Assert.Equal(string.Empty, chapter.ContentMarkdown);
        Assert.Equal(0, chapter.WordCount);
    }

    [Fact]
    public async Task Adding_a_chapter_bumps_the_manuscript_so_the_library_reorders()
    {
        Manuscript manuscript = _manuscripts.Seed(createdAt: DateTimeOffset.UnixEpoch);
        _clock.Advance(TimeSpan.FromDays(2));

        await _chapterUseCases.AddAsync(manuscript.Id, "One");

        Assert.Equal(1, _manuscripts.TouchCount);
        Assert.Equal(_clock.Now, _manuscripts.Peek(manuscript.Id)!.ModifiedAt);
    }

    [Fact]
    public async Task Adding_a_chapter_to_a_manuscript_that_is_gone_reports_not_found()
    {
        DomainResult<Chapter> result = await _chapterUseCases.AddAsync(Guid.NewGuid(), "One");

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
        Assert.Equal(0, _manuscripts.TouchCount);
    }

    [Fact]
    public async Task An_untitled_chapter_is_rejected_before_the_manuscript_is_touched()
    {
        Manuscript manuscript = _manuscripts.Seed();

        DomainResult<Chapter> result = await _chapterUseCases.AddAsync(manuscript.Id, "  ");

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
        Assert.Equal(0, _manuscripts.TouchCount);
    }

    // ---- Chapters: reorder (contracts/manuscript-service.md) ----

    [Fact]
    public async Task Reordering_assigns_contiguous_indices_from_zero()
    {
        Manuscript manuscript = _manuscripts.Seed();
        Chapter one = (await _chapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        Chapter two = (await _chapterUseCases.AddAsync(manuscript.Id, "Two")).Value;
        Chapter three = (await _chapterUseCases.AddAsync(manuscript.Id, "Three")).Value;

        DomainResult result = await _chapterUseCases.ReorderAsync(manuscript.Id, [three.Id, one.Id, two.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [new ChapterOrderAssignment(three.Id, 0), new ChapterOrderAssignment(one.Id, 1), new ChapterOrderAssignment(two.Id, 2)],
            _chapters.LastOrderApplied);
        Assert.Equal(["Three", "One", "Two"], _chapters.SummariesFor(manuscript.Id).Select(c => c.Title));
    }

    [Fact]
    public async Task Reordering_with_a_missing_chapter_is_a_validation_error_and_writes_nothing()
    {
        Manuscript manuscript = _manuscripts.Seed();
        Chapter one = (await _chapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        await _chapterUseCases.AddAsync(manuscript.Id, "Two");

        DomainResult result = await _chapterUseCases.ReorderAsync(manuscript.Id, [one.Id]);

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
        Assert.Equal(0, _chapters.ApplyOrderCount);
    }

    [Fact]
    public async Task Reordering_with_a_chapter_from_another_manuscript_is_a_validation_error()
    {
        Manuscript manuscript = _manuscripts.Seed("Mine");
        Manuscript other = _manuscripts.Seed("Theirs");
        Chapter mine = (await _chapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        Chapter theirs = (await _chapterUseCases.AddAsync(other.Id, "Intruder")).Value;

        DomainResult result = await _chapterUseCases.ReorderAsync(manuscript.Id, [mine.Id, theirs.Id]);

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
        Assert.Equal(0, _chapters.ApplyOrderCount);
    }

    [Fact]
    public async Task Reordering_with_a_duplicated_chapter_is_a_validation_error()
    {
        Manuscript manuscript = _manuscripts.Seed();
        Chapter one = (await _chapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        await _chapterUseCases.AddAsync(manuscript.Id, "Two");

        DomainResult result = await _chapterUseCases.ReorderAsync(manuscript.Id, [one.Id, one.Id]);

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
        Assert.Equal(0, _chapters.ApplyOrderCount);
    }

    // ---- Chapters: keyboard move (FR-019, SC-007) ----

    [Fact]
    public async Task Moving_a_chapter_up_swaps_it_with_its_predecessor()
    {
        Manuscript manuscript = _manuscripts.Seed();
        await _chapterUseCases.AddAsync(manuscript.Id, "One");
        Chapter two = (await _chapterUseCases.AddAsync(manuscript.Id, "Two")).Value;

        DomainResult result = await _chapterUseCases.MoveAsync(manuscript.Id, two.Id, -1);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Two", "One"], _chapters.SummariesFor(manuscript.Id).Select(c => c.Title));
    }

    [Fact]
    public async Task Moving_the_first_chapter_up_is_a_no_op_rather_than_a_failure()
    {
        Manuscript manuscript = _manuscripts.Seed();
        Chapter one = (await _chapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        await _chapterUseCases.AddAsync(manuscript.Id, "Two");

        DomainResult result = await _chapterUseCases.MoveAsync(manuscript.Id, one.Id, -1);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _chapters.ApplyOrderCount);
        Assert.Equal(["One", "Two"], _chapters.SummariesFor(manuscript.Id).Select(c => c.Title));
    }

    [Fact]
    public async Task Moving_a_chapter_that_is_gone_reports_not_found()
    {
        Manuscript manuscript = _manuscripts.Seed();
        await _chapterUseCases.AddAsync(manuscript.Id, "One");

        DomainResult result = await _chapterUseCases.MoveAsync(manuscript.Id, Guid.NewGuid(), 1);

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    // ---- Chapters: rename and delete ----

    [Fact]
    public async Task Renaming_a_chapter_that_is_gone_reports_not_found()
    {
        DomainResult result = await _chapterUseCases.RenameAsync(Guid.NewGuid(), "Anything");

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task Deleting_a_chapter_closes_the_gap_in_the_ordering()
    {
        Manuscript manuscript = _manuscripts.Seed();
        await _chapterUseCases.AddAsync(manuscript.Id, "One");
        Chapter two = (await _chapterUseCases.AddAsync(manuscript.Id, "Two")).Value;
        await _chapterUseCases.AddAsync(manuscript.Id, "Three");

        DomainResult result = await _chapterUseCases.DeleteAsync(two.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal([0, 1], _chapters.SummariesFor(manuscript.Id).Select(c => c.OrderIndex));
        Assert.Equal(["One", "Three"], _chapters.SummariesFor(manuscript.Id).Select(c => c.Title));
    }

    [Fact]
    public async Task Deleting_a_chapter_that_is_gone_reports_not_found()
    {
        DomainResult result = await _chapterUseCases.DeleteAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task Opening_a_chapter_that_is_gone_reports_not_found()
    {
        DomainResult<ChapterContent> result = await _chapterUseCases.GetContentAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    // ---- Manuscript word count (FR-009) ----

    [Fact]
    public async Task The_manuscript_word_count_is_the_sum_of_its_chapters()
    {
        Manuscript manuscript = _manuscripts.Seed();
        Chapter one = (await _chapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        Chapter two = (await _chapterUseCases.AddAsync(manuscript.Id, "Two")).Value;

        await _chapters.CommitAutoSaveAsync(new AutoSaveCommit(one.Id, "a b c", 3, _clock.Now, _clock.Today));
        await _chapters.CommitAutoSaveAsync(new AutoSaveCommit(two.Id, "d e", 2, _clock.Now, _clock.Today));

        Assert.Equal(5, await _chapterUseCases.GetManuscriptWordCountAsync(manuscript.Id));
    }
}
