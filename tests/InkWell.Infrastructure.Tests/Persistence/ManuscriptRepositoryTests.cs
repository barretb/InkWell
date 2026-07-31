using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Tests.Fixtures;
using SQLite;

namespace InkWell.Infrastructure.Tests.Persistence;

/// <summary>
/// US1 · contracts/manuscript-service.md — create/list round-trip, rename, reorder across a
/// restart, and cascade delete with no orphan rows.
/// </summary>
public class ManuscriptRepositoryTests
{
    [Fact]
    public async Task A_created_manuscript_appears_in_the_library()
    {
        await using var fixture = new StoreFixture();

        DomainResult<Manuscript> created = await fixture.ManuscriptUseCases.CreateAsync("The Long Winter");

        Assert.True(created.IsSuccess);
        IReadOnlyList<ManuscriptSummary> library = await fixture.ManuscriptUseCases.ListAsync();
        ManuscriptSummary only = Assert.Single(library);
        Assert.Equal("The Long Winter", only.Title);
        Assert.Equal(0, only.ChapterCount);
        Assert.Equal(0, only.WordCount);
    }

    [Fact]
    public async Task An_empty_library_is_an_empty_list_not_an_error()
    {
        await using var fixture = new StoreFixture();

        Assert.Empty(await fixture.ManuscriptUseCases.ListAsync());
    }

    [Fact]
    public async Task The_library_is_ordered_newest_modified_first()
    {
        await using var fixture = new StoreFixture();
        DomainResult<Manuscript> first = await fixture.ManuscriptUseCases.CreateAsync("First");
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        DomainResult<Manuscript> second = await fixture.ManuscriptUseCases.CreateAsync("Second");

        IReadOnlyList<ManuscriptSummary> library = await fixture.ManuscriptUseCases.ListAsync();

        Assert.Equal(second.Value.Id, library[0].Id);
        Assert.Equal(first.Value.Id, library[1].Id);
    }

    [Fact]
    public async Task A_rename_persists_and_bumps_the_modified_stamp()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Working Title")).Value;
        DateTimeOffset before = manuscript.ModifiedAt;
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        DomainResult renamed = await fixture.ManuscriptUseCases.RenameAsync(manuscript.Id, "  The Long Winter  ");

        Assert.True(renamed.IsSuccess);
        Manuscript? reloaded = await fixture.Manuscripts.GetAsync(manuscript.Id);
        Assert.Equal("The Long Winter", reloaded!.Title);
        Assert.True(reloaded.ModifiedAt > before);
    }

    [Fact]
    public async Task Renaming_a_manuscript_that_is_gone_reports_not_found()
    {
        await using var fixture = new StoreFixture();

        DomainResult result = await fixture.ManuscriptUseCases.RenameAsync(Guid.NewGuid(), "Anything");

        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task An_invalid_title_is_rejected_before_it_reaches_the_store()
    {
        await using var fixture = new StoreFixture();

        DomainResult<Manuscript> result = await fixture.ManuscriptUseCases.CreateAsync("   ");

        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
        Assert.Empty(await fixture.ManuscriptUseCases.ListAsync());
    }

    [Fact]
    public async Task Chapters_and_their_order_survive_closing_and_reopening_the_app()
    {
        // US1 independent test: three chapters, prose in each, reordered, then a restart.
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;

        Chapter one = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        Chapter two = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Two")).Value;
        Chapter three = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Three")).Value;

        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(one.Id, "The snow came early.", 4, fixture.Clock.Now, fixture.Clock.Today));
        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(two.Id, "It fell for nine days.", 5, fixture.Clock.Now, fixture.Clock.Today));
        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(three.Id, "Then it stopped.", 3, fixture.Clock.Now, fixture.Clock.Today));

        await fixture.ChapterUseCases.ReorderAsync(manuscript.Id, [three.Id, one.Id, two.Id]);

        await fixture.RestartAsync();

        IReadOnlyList<ChapterSummary> chapters = await fixture.ChapterUseCases.ListAsync(manuscript.Id);
        Assert.Equal([three.Id, one.Id, two.Id], chapters.Select(c => c.Id));
        Assert.Equal([0, 1, 2], chapters.Select(c => c.OrderIndex));

        ChapterContent content = (await fixture.ChapterUseCases.GetContentAsync(one.Id)).Value;
        Assert.Equal("The snow came early.", content.ContentMarkdown);
    }

    [Fact]
    public async Task A_reorder_that_does_not_name_every_chapter_is_rejected()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Winter")).Value;
        Chapter one = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        _ = await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Two");

        DomainResult result = await fixture.ChapterUseCases.ReorderAsync(manuscript.Id, [one.Id]);

        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
        Assert.Equal([0, 1], (await fixture.ChapterUseCases.ListAsync(manuscript.Id)).Select(c => c.OrderIndex));
    }

    [Fact]
    public async Task Moving_a_chapter_one_place_reorders_it()
    {
        // The keyboard-operable path behind FR-019 / SC-007.
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Winter")).Value;
        Chapter one = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        Chapter two = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Two")).Value;

        await fixture.ChapterUseCases.MoveAsync(manuscript.Id, two.Id, -1);

        Assert.Equal([two.Id, one.Id], (await fixture.ChapterUseCases.ListAsync(manuscript.Id)).Select(c => c.Id));
    }

    [Fact]
    public async Task Moving_the_first_chapter_up_changes_nothing()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Winter")).Value;
        Chapter one = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        Chapter two = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Two")).Value;

        DomainResult result = await fixture.ChapterUseCases.MoveAsync(manuscript.Id, one.Id, -1);

        Assert.True(result.IsSuccess);
        Assert.Equal([one.Id, two.Id], (await fixture.ChapterUseCases.ListAsync(manuscript.Id)).Select(c => c.Id));
    }

    [Fact]
    public async Task Deleting_a_chapter_closes_the_gap_in_the_ordering()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Winter")).Value;
        Chapter one = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        Chapter two = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Two")).Value;
        Chapter three = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Three")).Value;

        await fixture.ChapterUseCases.DeleteAsync(two.Id);

        IReadOnlyList<ChapterSummary> chapters = await fixture.ChapterUseCases.ListAsync(manuscript.Id);
        Assert.Equal([one.Id, three.Id], chapters.Select(c => c.Id));
        Assert.Equal([0, 1], chapters.Select(c => c.OrderIndex));
    }

    [Fact]
    public async Task Deleting_a_manuscript_leaves_no_orphan_rows()
    {
        // The data-layer half of SC-008.
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Winter")).Value;
        Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        await fixture.Images.AddAsync(
            new InlineImageInsert(chapter.Id, [1, 2, 3, 4], "image/png", "a mill"),
            fixture.Clock.Now);

        DomainResult deleted = await fixture.ManuscriptUseCases.DeleteAsync(manuscript.Id);

        Assert.True(deleted.IsSuccess);
        Assert.Empty(await fixture.ManuscriptUseCases.ListAsync());
        Assert.Empty(await fixture.ChapterUseCases.ListAsync(manuscript.Id));
        Assert.Empty(await fixture.Images.ListReferencesAsync(chapter.Id));
    }

    [Fact]
    public async Task A_new_chapter_is_appended_after_the_last_one()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Winter")).Value;

        await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One");
        await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Two");
        Chapter third = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Three")).Value;

        Assert.Equal(2, third.OrderIndex);
    }

    [Fact]
    public async Task Adding_a_chapter_to_a_manuscript_that_is_gone_reports_not_found()
    {
        await using var fixture = new StoreFixture();

        DomainResult<Chapter> result = await fixture.ChapterUseCases.AddAsync(Guid.NewGuid(), "One");

        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task Opening_a_manuscript_does_not_load_chapter_prose()
    {
        // SC-004: a 150,000-word manuscript must open without reading 150,000 words.
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Winter")).Value;
        Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(chapter.Id, "The snow came early.", 4, fixture.Clock.Now, fixture.Clock.Today));

        ManuscriptDetail detail = (await fixture.ManuscriptUseCases.GetAsync(manuscript.Id)).Value;

        ChapterSummary summary = Assert.Single(detail.Chapters);
        Assert.Equal(4, summary.WordCount);
        Assert.Equal(4, detail.WordCount);
        Assert.Equal("One", summary.Title);
    }

    [Fact]
    public async Task Chapters_of_one_manuscript_never_appear_in_another()
    {
        await using var fixture = new StoreFixture();
        Manuscript first = (await fixture.ManuscriptUseCases.CreateAsync("First")).Value;
        Manuscript second = (await fixture.ManuscriptUseCases.CreateAsync("Second")).Value;
        await fixture.ChapterUseCases.AddAsync(first.Id, "Only chapter");

        Assert.Empty(await fixture.ChapterUseCases.ListAsync(second.Id));
    }

    [Fact]
    public async Task The_manuscript_word_count_is_the_sum_of_its_chapters()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Winter")).Value;
        Chapter one = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        Chapter two = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Two")).Value;
        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(one.Id, "one two three", 3, fixture.Clock.Now, fixture.Clock.Today));
        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(two.Id, "four five", 2, fixture.Clock.Now, fixture.Clock.Today));

        Assert.Equal(5, await fixture.ChapterUseCases.GetManuscriptWordCountAsync(manuscript.Id));
    }

    [Fact]
    public async Task A_transaction_that_fails_leaves_no_partial_state()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Winter")).Value;

        // A chapter pointing at a manuscript that does not exist violates the foreign key.
        await Assert.ThrowsAnyAsync<SQLiteException>(() => fixture.Chapters.AddAsync(new Chapter
        {
            Id = Guid.NewGuid(),
            ManuscriptId = Guid.NewGuid(),
            Title = "Orphan",
            CreatedAt = fixture.Clock.Now,
            ModifiedAt = fixture.Clock.Now,
        }));

        Assert.Empty(await fixture.ChapterUseCases.ListAsync(manuscript.Id));
    }
}
