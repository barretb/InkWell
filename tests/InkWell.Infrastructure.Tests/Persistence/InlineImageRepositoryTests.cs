using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Persistence;
using InkWell.Infrastructure.Tests.Fixtures;

namespace InkWell.Infrastructure.Tests.Persistence;

/// <summary>
/// US1 · FR-003a — image bytes are copied into the encrypted store, so the manuscript is
/// self-contained and survives the source file being moved or deleted.
/// </summary>
public class InlineImageRepositoryTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    [Fact]
    public async Task An_inserted_image_is_readable_back_as_a_data_uri()
    {
        await using var fixture = new StoreFixture();
        Guid chapterId = await SeedChapterAsync(fixture);

        InlineImageReference reference = await fixture.Images.AddAsync(
            new InlineImageInsert(chapterId, PngBytes, "image/png", "the frozen mill"), fixture.Clock.Now);

        Assert.StartsWith("data:image/png;base64,", reference.DataUri, StringComparison.Ordinal);
        Assert.Equal("the frozen mill", reference.AltText);
        Assert.False(reference.IsMissingAltText);

        InlineImageReference listed = Assert.Single(await fixture.Images.ListReferencesAsync(chapterId));
        Assert.Equal(reference.Id, listed.Id);
        Assert.Equal(reference.DataUri, listed.DataUri);
    }

    [Fact]
    public async Task The_bytes_are_copied_so_the_manuscript_does_not_depend_on_the_source_file()
    {
        // Simulates the writer deleting the file they dragged in: the caller's array is cleared
        // after insert, and the stored image must be unaffected.
        await using var fixture = new StoreFixture();
        Guid chapterId = await SeedChapterAsync(fixture);
        byte[] source = [.. PngBytes];

        InlineImageReference reference = await fixture.Images.AddAsync(
            new InlineImageInsert(chapterId, source, "image/png", null), fixture.Clock.Now);
        Array.Clear(source);

        InlineImage? stored = await fixture.Images.GetAsync(reference.Id);
        Assert.Equal(PngBytes, stored!.Bytes);
        Assert.Equal(PngBytes.Length, stored.ByteLength);
    }

    [Fact]
    public async Task An_image_survives_a_restart()
    {
        await using var fixture = new StoreFixture();
        Guid chapterId = await SeedChapterAsync(fixture);
        InlineImageReference reference = await fixture.Images.AddAsync(
            new InlineImageInsert(chapterId, PngBytes, "image/png", "a mill"), fixture.Clock.Now);

        await fixture.RestartAsync();

        InlineImage? stored = await fixture.Images.GetAsync(reference.Id);
        Assert.Equal(PngBytes, stored!.Bytes);
    }

    [Fact]
    public async Task An_image_without_alt_text_is_stored_but_flagged()
    {
        // FR-019 edge case: permitted, never silently accepted as compliant.
        await using var fixture = new StoreFixture();
        Guid chapterId = await SeedChapterAsync(fixture);
        Guid manuscriptId = (await fixture.Chapters.GetAsync(chapterId))!.ManuscriptId;

        InlineImageReference reference = await fixture.Images.AddAsync(
            new InlineImageInsert(chapterId, PngBytes, "image/png", null), fixture.Clock.Now);

        Assert.True(reference.IsMissingAltText);
        Assert.Equal([reference.Id], await fixture.Images.ListMissingAltTextAsync(manuscriptId));
    }

    [Fact]
    public async Task Supplying_alt_text_later_clears_the_accessibility_gap()
    {
        await using var fixture = new StoreFixture();
        Guid chapterId = await SeedChapterAsync(fixture);
        Guid manuscriptId = (await fixture.Chapters.GetAsync(chapterId))!.ManuscriptId;
        InlineImageReference reference = await fixture.Images.AddAsync(
            new InlineImageInsert(chapterId, PngBytes, "image/png", null), fixture.Clock.Now);

        Assert.True(await fixture.Images.SetAltTextAsync(reference.Id, "the frozen mill"));

        Assert.Empty(await fixture.Images.ListMissingAltTextAsync(manuscriptId));
    }

    [Fact]
    public async Task Whitespace_alt_text_still_counts_as_missing()
    {
        await using var fixture = new StoreFixture();
        Guid chapterId = await SeedChapterAsync(fixture);
        Guid manuscriptId = (await fixture.Chapters.GetAsync(chapterId))!.ManuscriptId;

        InlineImageReference reference = await fixture.Images.AddAsync(
            new InlineImageInsert(chapterId, PngBytes, "image/png", "   "), fixture.Clock.Now);

        Assert.Equal([reference.Id], await fixture.Images.ListMissingAltTextAsync(manuscriptId));
    }

    [Fact]
    public async Task Deleting_a_chapter_deletes_its_images()
    {
        await using var fixture = new StoreFixture();
        Guid chapterId = await SeedChapterAsync(fixture);
        InlineImageReference reference = await fixture.Images.AddAsync(
            new InlineImageInsert(chapterId, PngBytes, "image/png", "a mill"), fixture.Clock.Now);

        await fixture.ChapterUseCases.DeleteAsync(chapterId);

        Assert.Null(await fixture.Images.GetAsync(reference.Id));
    }

    [Fact]
    public async Task An_empty_image_is_rejected()
    {
        await using var fixture = new StoreFixture();
        Guid chapterId = await SeedChapterAsync(fixture);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Images.AddAsync(
            new InlineImageInsert(chapterId, [], "image/png", null), fixture.Clock.Now));
    }

    [Fact]
    public async Task An_oversized_image_is_rejected_rather_than_bloating_the_store()
    {
        await using var fixture = new StoreFixture();
        Guid chapterId = await SeedChapterAsync(fixture);
        var huge = new byte[InlineImageRepository.MaxImageBytes + 1];

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Images.AddAsync(
            new InlineImageInsert(chapterId, huge, "image/png", null), fixture.Clock.Now));
    }

    [Fact]
    public async Task Opening_a_chapter_returns_its_images_alongside_the_markdown()
    {
        await using var fixture = new StoreFixture();
        Guid chapterId = await SeedChapterAsync(fixture);
        InlineImageReference reference = await fixture.Images.AddAsync(
            new InlineImageInsert(chapterId, PngBytes, "image/png", "a mill"), fixture.Clock.Now);
        await fixture.Chapters.CommitAutoSaveAsync(new AutoSaveCommit(
            chapterId,
            $"Before ![a mill](inkwell-img://{reference.Id}) after",
            2,
            fixture.Clock.Now,
            fixture.Clock.Today));

        ChapterContent content = (await fixture.ChapterUseCases.GetContentAsync(chapterId)).Value;

        Assert.Single(content.Images);
        Assert.Contains(reference.Id.ToString(), content.ContentMarkdown, StringComparison.Ordinal);
    }

    private static async Task<Guid> SeedChapterAsync(StoreFixture fixture)
    {
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;
        Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "One")).Value;
        return chapter.Id;
    }
}
