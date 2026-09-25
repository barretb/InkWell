using System.Text;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Infrastructure.Tests.Fixtures;

namespace InkWell.Infrastructure.Tests.Persistence;

/// <summary>
/// T110 · FR-018, SC-008 — the writer can see everything InkWell holds and can erase it, with
/// nothing recoverable afterwards.
/// </summary>
public class DataControlsTests
{
    private static async Task<Guid> SeedAsync(StoreFixture fixture, string title = "The Long Winter")
    {
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync(title)).Value;

        await fixture.WriteChapterWithImagesAsync(manuscript.Id, "Snowfall", 2);
        await fixture.WriteChapterWithImagesAsync(manuscript.Id, "The Mill", 1);
        await fixture.ReferenceUseCases.CreateCharacterAsync(manuscript.Id, "Elin", "Miller's daughter.");
        await fixture.ReferenceUseCases.CreateCharacterAsync(manuscript.Id, "Tomas", null);
        await fixture.ReferenceUseCases.CreatePlotThreadAsync(manuscript.Id, "The mill fire", "Started by Elin.");
        await fixture.GoalUseCases.SetGoalAsync(manuscript.Id, 500);

        return manuscript.Id;
    }

    // ---- View everything stored (FR-018) ----

    [Fact]
    public async Task The_inventory_counts_every_kind_of_thing_the_app_holds()
    {
        // "View all of your data" is only meaningful if nothing is left off the list.
        await using var fixture = new StoreFixture();
        Guid manuscriptId = await SeedAsync(fixture);

        DataInventory inventory = await fixture.DataControls.GetInventoryAsync();

        ManuscriptDataInventory entry = Assert.Single(inventory.Manuscripts);
        Assert.Equal(manuscriptId, entry.ManuscriptId);
        Assert.Equal("The Long Winter", entry.Title);
        Assert.Equal(2, entry.ChapterCount);
        Assert.Equal(3, entry.InlineImageCount);
        Assert.True(entry.InlineImageBytes > 0);
        Assert.Equal(2, entry.CharacterCount);
        Assert.Equal(1, entry.PlotThreadCount);
        Assert.True(entry.HasDailyGoal);
        Assert.True(entry.WritingRecordCount >= 1);
        Assert.True(entry.WordCount > 0);
    }

    [Fact]
    public async Task The_inventory_names_the_file_and_its_size_so_the_promise_is_checkable()
    {
        // "It is all on your device" is a claim the writer should be able to verify.
        await using var fixture = new StoreFixture();
        await SeedAsync(fixture);

        DataInventory inventory = await fixture.DataControls.GetInventoryAsync();

        Assert.Equal(fixture.DatabasePath, inventory.DatabasePath);
        Assert.True(File.Exists(inventory.DatabasePath));
        Assert.True(inventory.DatabaseByteLength > 0);
    }

    [Fact]
    public async Task Each_manuscript_is_counted_separately()
    {
        await using var fixture = new StoreFixture();
        await SeedAsync(fixture, "The Long Winter");
        Manuscript second = (await fixture.ManuscriptUseCases.CreateAsync("A Second Novel")).Value;
        await fixture.WriteChapterWithImagesAsync(second.Id, "Only Chapter", 0);

        DataInventory inventory = await fixture.DataControls.GetInventoryAsync();

        Assert.Equal(2, inventory.Manuscripts.Count);
        ManuscriptDataInventory other = inventory.Manuscripts.Single(m => m.Title == "A Second Novel");
        Assert.Equal(1, other.ChapterCount);
        Assert.Equal(0, other.InlineImageCount);
        Assert.False(other.HasDailyGoal);
    }

    [Fact]
    public async Task An_empty_app_reports_an_empty_inventory_rather_than_failing()
    {
        await using var fixture = new StoreFixture();

        DataInventory inventory = await fixture.DataControls.GetInventoryAsync();

        Assert.Empty(inventory.Manuscripts);
    }

    // ---- Delete one manuscript ----

    [Fact]
    public async Task Deleting_one_manuscript_takes_everything_it_owns_and_nothing_else()
    {
        await using var fixture = new StoreFixture();
        Guid doomed = await SeedAsync(fixture, "The Long Winter");
        Guid kept = await SeedAsync(fixture, "A Second Novel");

        bool deleted = await fixture.DataControls.DeleteManuscriptDataAsync(doomed);

        Assert.True(deleted);
        DataInventory inventory = await fixture.DataControls.GetInventoryAsync();
        ManuscriptDataInventory survivor = Assert.Single(inventory.Manuscripts);
        Assert.Equal(kept, survivor.ManuscriptId);
        Assert.Equal(2, survivor.ChapterCount);
        Assert.Equal(3, survivor.InlineImageCount);
        Assert.Equal(2, survivor.CharacterCount);
    }

    [Fact]
    public async Task Deleting_a_manuscript_that_is_gone_reports_it_rather_than_throwing()
    {
        await using var fixture = new StoreFixture();

        Assert.False(await fixture.DataControls.DeleteManuscriptDataAsync(Guid.NewGuid()));
    }

    // ---- Delete everything (SC-008) ----

    [Fact]
    public async Task Deleting_all_data_empties_every_table()
    {
        await using var fixture = new StoreFixture();
        await SeedAsync(fixture, "The Long Winter");
        await SeedAsync(fixture, "A Second Novel");

        await fixture.DataControls.DeleteAllDataAsync();

        DataInventory inventory = await fixture.DataControls.GetInventoryAsync();
        Assert.Empty(inventory.Manuscripts);
        Assert.Empty(await fixture.ManuscriptUseCases.ListAsync());
    }

    [Fact]
    public async Task Deleting_all_data_removes_the_encryption_key()
    {
        // The step that makes it irreversible: whatever bytes survive anywhere are ciphertext that
        // nobody — including this app — can open again (SC-008).
        await using var fixture = new StoreFixture();
        await SeedAsync(fixture);

        Assert.NotNull(await fixture.SecureStore.GetAsync(Infrastructure.Security.KeyStore.SecureStoreKey));

        await fixture.DataControls.DeleteAllDataAsync();

        Assert.Null(await fixture.SecureStore.GetAsync(Infrastructure.Security.KeyStore.SecureStoreKey));
    }

    [Fact]
    public async Task No_prose_survives_in_the_file_after_deleting_all_data()
    {
        // Deleting rows is not enough on its own: SQLite leaves freed pages intact until they are
        // reused, so without the VACUUM the writer's novel would still be sitting in a file they
        // were told was empty.
        const string prose = "ElinWatchedTheMillBurnFromTheRidgeUniqueMarker";
        const string characterNote = "ElinIsSecretlyTheMillersDaughterUniqueMarker";
        const string chapterTitle = "TheRidgeChapterTitleUniqueMarker";

        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("AVeryPrivateNovelUniqueMarker")).Value;
        Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, chapterTitle)).Value;
        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(chapter.Id, prose, 1, fixture.Clock.Now, fixture.Clock.Today));
        await fixture.ReferenceUseCases.CreateCharacterAsync(manuscript.Id, "Elin", characterNote);

        await fixture.DataControls.DeleteAllDataAsync();

        Assert.False(File.Exists(fixture.DatabasePath));
        // Opening the inventory creates a new empty store with a fresh key.
        Assert.Empty((await fixture.DataControls.GetInventoryAsync()).Manuscripts);
        byte[] raw = await fixture.ReadDatabaseBytesAsync();
        string asText = Encoding.UTF8.GetString(raw);

        Assert.DoesNotContain(prose, asText, StringComparison.Ordinal);
        Assert.DoesNotContain(characterNote, asText, StringComparison.Ordinal);
        Assert.DoesNotContain(chapterTitle, asText, StringComparison.Ordinal);
        Assert.DoesNotContain("AVeryPrivateNovelUniqueMarker", asText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_image_bytes_survive_after_deleting_all_data()
    {
        await using var fixture = new StoreFixture();
        Manuscript manuscript = (await fixture.ManuscriptUseCases.CreateAsync("Novel")).Value;
        Chapter chapter = (await fixture.ChapterUseCases.AddAsync(manuscript.Id, "Snowfall")).Value;

        byte[] secret = Encoding.ASCII.GetBytes("ThisImageIsPrivateAndMustNotSurviveDeletion");
        await fixture.Images.AddAsync(
            new InlineImageInsert(chapter.Id, secret, "image/png", "A private picture"), fixture.Clock.Now);

        await fixture.DataControls.DeleteAllDataAsync();

        Assert.False(File.Exists(fixture.DatabasePath));
        // Opening the inventory creates a new empty store with a fresh key.
        Assert.Empty((await fixture.DataControls.GetInventoryAsync()).Manuscripts);
        byte[] raw = await fixture.ReadDatabaseBytesAsync();
        Assert.DoesNotContain(
            "ThisImageIsPrivateAndMustNotSurviveDeletion",
            Encoding.UTF8.GetString(raw),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_app_still_works_after_everything_is_deleted()
    {
        // Erasing your data should leave a working app, not a broken one the writer has to reinstall.
        await using var fixture = new StoreFixture();
        await SeedAsync(fixture);

        await fixture.DataControls.DeleteAllDataAsync();

        Manuscript fresh = (await fixture.ManuscriptUseCases.CreateAsync("Starting Again")).Value;
        Chapter chapter = (await fixture.ChapterUseCases.AddAsync(fresh.Id, "Chapter One")).Value;
        await fixture.Chapters.CommitAutoSaveAsync(
            new AutoSaveCommit(chapter.Id, "New words.", 2, fixture.Clock.Now, fixture.Clock.Today));

        await fixture.RestartAsync();
        DataInventory inventory = await fixture.DataControls.GetInventoryAsync();
        Assert.Single(inventory.Manuscripts);
        Assert.Equal("Starting Again", inventory.Manuscripts[0].Title);
        Assert.Equal("New words.", (await fixture.Chapters.GetContentAsync(chapter.Id))!.ContentMarkdown);
    }
    [Fact]
    public async Task Restarting_immediately_after_delete_creates_a_readable_empty_store()
    {
        await using var fixture = new StoreFixture();
        await SeedAsync(fixture);
        string? oldKey = await fixture.SecureStore.GetAsync(Infrastructure.Security.KeyStore.SecureStoreKey);
        await fixture.DataControls.DeleteAllDataAsync();
        await fixture.RestartAsync();
        Assert.Empty((await fixture.DataControls.GetInventoryAsync()).Manuscripts);
        string? newKey = await fixture.SecureStore.GetAsync(Infrastructure.Security.KeyStore.SecureStoreKey);
        Assert.NotNull(newKey);
        Assert.NotEqual(oldKey, newKey);
        await fixture.DataControls.DeleteAllDataAsync();
        await fixture.RestartAsync();
        Assert.Empty(await fixture.ManuscriptUseCases.ListAsync());
    }
}
