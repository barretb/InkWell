using InkWell.Application.Abstractions.Dtos;
using InkWell.Application.UseCases;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Tests.Fixtures;

namespace InkWell.Infrastructure.Tests.Persistence;

/// <summary>
/// US1 · FR-004, SC-003 — the writer never presses save, and an unexpected shutdown costs at most
/// the last moments of typing.
/// </summary>
public class AutoSaveTests
{
    private static AutoSaveOptions Fast => new(TimeSpan.FromMilliseconds(40));

    [Fact]
    public async Task A_pause_in_typing_commits_the_edit()
    {
        await using var fixture = new StoreFixture();
        (Guid _, Guid chapterId) = await SeedAsync(fixture);
        await using var coordinator = new AutoSaveCoordinator(fixture.Chapters, fixture.Clock, Fast);
        var saved = new TaskCompletionSource<AutoSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Saved += (_, result) => saved.TrySetResult(result);

        coordinator.QueueEdit(chapterId, "The snow came early that year.");

        AutoSaveResult result = await saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(6, result.ChapterWordCount);

        ChapterContent content = (await fixture.ChapterUseCases.GetContentAsync(chapterId)).Value;
        Assert.Equal("The snow came early that year.", content.ContentMarkdown);
    }

    [Fact]
    public async Task Flushing_commits_immediately()
    {
        await using var fixture = new StoreFixture();
        (Guid _, Guid chapterId) = await SeedAsync(fixture);
        await using var coordinator = new AutoSaveCoordinator(
            fixture.Chapters, fixture.Clock, new AutoSaveOptions(TimeSpan.FromMinutes(10)));

        coordinator.QueueEdit(chapterId, "Typed but not yet idle.");
        AutoSaveResult? result = await coordinator.FlushAsync();

        Assert.NotNull(result);
        Assert.False(coordinator.HasPendingEdit);
        ChapterContent content = (await fixture.ChapterUseCases.GetContentAsync(chapterId)).Value;
        Assert.Equal("Typed but not yet idle.", content.ContentMarkdown);
    }

    [Fact]
    public async Task Flushing_with_nothing_pending_does_nothing()
    {
        await using var fixture = new StoreFixture();
        await using var coordinator = new AutoSaveCoordinator(fixture.Chapters, fixture.Clock, Fast);

        Assert.Null(await coordinator.FlushAsync());
    }

    [Fact]
    public async Task Only_the_newest_content_is_committed()
    {
        await using var fixture = new StoreFixture();
        (Guid _, Guid chapterId) = await SeedAsync(fixture);
        await using var coordinator = new AutoSaveCoordinator(
            fixture.Chapters, fixture.Clock, new AutoSaveOptions(TimeSpan.FromMinutes(10)));

        coordinator.QueueEdit(chapterId, "first");
        coordinator.QueueEdit(chapterId, "second");
        coordinator.QueueEdit(chapterId, "third");
        await coordinator.FlushAsync();

        ChapterContent content = (await fixture.ChapterUseCases.GetContentAsync(chapterId)).Value;
        Assert.Equal("third", content.ContentMarkdown);
    }

    [Fact]
    public async Task The_word_count_is_recomputed_and_never_taken_from_the_editor()
    {
        // FR-009: the count drives the writer's daily goal, so it is derived from the prose.
        await using var fixture = new StoreFixture();
        (Guid _, Guid chapterId) = await SeedAsync(fixture);
        await using var coordinator = new AutoSaveCoordinator(
            fixture.Chapters, fixture.Clock, new AutoSaveOptions(TimeSpan.FromMinutes(10)));

        coordinator.QueueEdit(chapterId, "## Chapter One\n\nHe was **never** coming back. ![a mill](inkwell-img://x)");
        AutoSaveResult? result = await coordinator.FlushAsync();

        // "Chapter One" (2) + "He was never coming back." (5); the image contributes nothing.
        Assert.Equal(7, result!.ChapterWordCount);
    }

    [Fact]
    public async Task An_edit_survives_a_restart()
    {
        // SC-003: reopening restores the auto-saved content.
        await using var fixture = new StoreFixture();
        (Guid _, Guid chapterId) = await SeedAsync(fixture);
        await using (var coordinator = new AutoSaveCoordinator(
            fixture.Chapters, fixture.Clock, new AutoSaveOptions(TimeSpan.FromMinutes(10))))
        {
            coordinator.QueueEdit(chapterId, "Words the writer never explicitly saved.");
            await coordinator.FlushAsync();
        }

        await fixture.RestartAsync();

        ChapterContent content = (await fixture.ChapterUseCases.GetContentAsync(chapterId)).Value;
        Assert.Equal("Words the writer never explicitly saved.", content.ContentMarkdown);
    }

    [Fact]
    public async Task Disposing_commits_whatever_was_still_pending()
    {
        // The app-suspend path: closing must not discard the last sentence.
        await using var fixture = new StoreFixture();
        (Guid _, Guid chapterId) = await SeedAsync(fixture);

        var coordinator = new AutoSaveCoordinator(
            fixture.Chapters, fixture.Clock, new AutoSaveOptions(TimeSpan.FromMinutes(10)));
        coordinator.QueueEdit(chapterId, "The very last thing typed.");
        await coordinator.DisposeAsync();

        ChapterContent content = (await fixture.ChapterUseCases.GetContentAsync(chapterId)).Value;
        Assert.Equal("The very last thing typed.", content.ContentMarkdown);
    }

    [Fact]
    public async Task Switching_chapters_does_not_lose_the_previous_chapters_edit()
    {
        await using var fixture = new StoreFixture();
        (Guid manuscriptId, Guid firstChapterId) = await SeedAsync(fixture);
        Chapter second = (await fixture.ChapterUseCases.AddAsync(manuscriptId, "Two")).Value;

        await using var coordinator = new AutoSaveCoordinator(
            fixture.Chapters, fixture.Clock, new AutoSaveOptions(TimeSpan.FromMinutes(10)));

        coordinator.QueueEdit(firstChapterId, "words for the first chapter");
        coordinator.QueueEdit(second.Id, "words for the second chapter");
        await coordinator.FlushAsync();

        // Give the implicit commit of the superseded chapter a moment to land.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            ChapterContent probe = (await fixture.ChapterUseCases.GetContentAsync(firstChapterId)).Value;
            if (probe.ContentMarkdown.Length > 0)
            {
                break;
            }

            await Task.Delay(20);
        }

        ChapterContent first = (await fixture.ChapterUseCases.GetContentAsync(firstChapterId)).Value;
        ChapterContent secondContent = (await fixture.ChapterUseCases.GetContentAsync(second.Id)).Value;
        Assert.Equal("words for the first chapter", first.ContentMarkdown);
        Assert.Equal("words for the second chapter", secondContent.ContentMarkdown);
    }

    [Fact]
    public async Task Saving_a_chapter_that_was_deleted_reports_no_result_rather_than_throwing()
    {
        await using var fixture = new StoreFixture();
        (Guid _, Guid chapterId) = await SeedAsync(fixture);
        await fixture.ChapterUseCases.DeleteAsync(chapterId);

        await using var coordinator = new AutoSaveCoordinator(
            fixture.Chapters, fixture.Clock, new AutoSaveOptions(TimeSpan.FromMinutes(10)));
        coordinator.QueueEdit(chapterId, "into the void");

        Assert.Null(await coordinator.FlushAsync());
    }

    [Fact]
    public async Task A_commit_bumps_the_manuscripts_modified_stamp()
    {
        await using var fixture = new StoreFixture();
        (Guid manuscriptId, Guid chapterId) = await SeedAsync(fixture);
        DateTimeOffset before = (await fixture.Manuscripts.GetAsync(manuscriptId))!.ModifiedAt;
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));

        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(chapterId, "new words", 2, fixture.Clock.Now, fixture.Clock.Today));

        Assert.True((await fixture.Manuscripts.GetAsync(manuscriptId))!.ModifiedAt > before);
    }

    private static async Task<(Guid ManuscriptId, Guid ChapterId)> SeedAsync(StoreFixture fixture)
    {
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;
        Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        return (manuscript.Id, chapter.Id);
    }
}
