using InkWell.Infrastructure.Persistence;
using InkWell.Infrastructure.Tests.Fixtures;
using SQLite;

namespace InkWell.Infrastructure.Tests.Persistence;

/// <summary>
/// The schema in data-model.md: every table, the indexes that keep a 50-chapter manuscript cheap to
/// open, the uniqueness rules, and the cascade deletes that "delete all my data" depends on
/// (FR-018, SC-008).
/// </summary>
public class SchemaMigrationTests
{
    [Fact]
    public async Task Creates_every_table()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();

        List<string> tables = await connection.QueryScalarsAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table'");

        foreach (string expected in DatabaseMigrator.TableNames)
        {
            Assert.Contains(expected, tables);
        }
    }

    [Fact]
    public async Task Creates_the_query_indexes()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();

        List<string> indexes = await connection.QueryScalarsAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'index' AND name IS NOT NULL");

        Assert.Contains("IX_Chapter_Manuscript_Order", indexes);
        Assert.Contains("IX_InlineImage_Chapter", indexes);
        Assert.Contains("IX_Character_Manuscript", indexes);
        Assert.Contains("IX_PlotThread_Manuscript", indexes);
        Assert.Contains("IX_DailyWritingRecord_Manuscript_Date", indexes);
    }

    [Fact]
    public async Task Records_the_schema_version()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();

        Assert.Equal(DatabaseMigrator.CurrentVersion, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
    }

    [Fact]
    public async Task Migrating_twice_is_harmless()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();

        await DatabaseMigrator.MigrateAsync(connection);
        await DatabaseMigrator.MigrateAsync(connection);

        Assert.Equal(DatabaseMigrator.CurrentVersion, await connection.ExecuteScalarAsync<int>("PRAGMA user_version"));
    }

    [Fact]
    public async Task Deleting_a_manuscript_cascades_to_every_child_row()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();
        (string manuscriptId, string chapterId) = await SeedFullManuscriptAsync(connection);

        await connection.ExecuteAsync("DELETE FROM Manuscript WHERE Id = ?", manuscriptId);

        Assert.Equal(0, await CountAsync(connection, "Chapter"));
        Assert.Equal(0, await CountAsync(connection, "InlineImage"));
        Assert.Equal(0, await CountAsync(connection, "Character"));
        Assert.Equal(0, await CountAsync(connection, "PlotThread"));
        Assert.Equal(0, await CountAsync(connection, "DailyGoal"));
        Assert.Equal(0, await CountAsync(connection, "DailyWritingRecord"));
        Assert.NotEqual(string.Empty, chapterId);
    }

    [Fact]
    public async Task Deleting_a_chapter_cascades_to_its_images_only()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();
        (string manuscriptId, string chapterId) = await SeedFullManuscriptAsync(connection);

        await connection.ExecuteAsync("DELETE FROM Chapter WHERE Id = ?", chapterId);

        Assert.Equal(0, await CountAsync(connection, "InlineImage"));
        Assert.Equal(1, await CountAsync(connection, "Manuscript"));
        Assert.Equal(1, await CountAsync(connection, "Character"));
        Assert.NotEqual(string.Empty, manuscriptId);
    }

    [Fact]
    public async Task A_manuscript_may_hold_only_one_daily_goal()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();
        (string manuscriptId, _) = await SeedFullManuscriptAsync(connection);

        await Assert.ThrowsAnyAsync<SQLiteException>(() => connection.ExecuteAsync(
            "INSERT INTO DailyGoal (Id, ManuscriptId, TargetWords, IsActive, CreatedAt, ModifiedAt) VALUES (?, ?, 750, 1, 1, 1)",
            Guid.NewGuid().ToString(), manuscriptId));
    }

    [Fact]
    public async Task A_manuscript_may_hold_only_one_record_per_day()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();
        (string manuscriptId, _) = await SeedFullManuscriptAsync(connection);

        await Assert.ThrowsAnyAsync<SQLiteException>(() => connection.ExecuteAsync(
            "INSERT INTO DailyWritingRecord (Id, ManuscriptId, Date, WordsWritten, GoalTarget, GoalMet) VALUES (?, ?, '2026-03-14', 10, 500, 0)",
            Guid.NewGuid().ToString(), manuscriptId));
    }

    [Fact]
    public async Task A_chapter_cannot_belong_to_a_manuscript_that_does_not_exist()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();

        await Assert.ThrowsAnyAsync<SQLiteException>(() => connection.ExecuteAsync(
            "INSERT INTO Chapter (Id, ManuscriptId, Title, ContentMarkdown, OrderIndex, WordCount, CreatedAt, ModifiedAt) " +
            "VALUES (?, ?, 'Orphan', '', 0, 0, 1, 1)",
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));
    }

    private static async Task<int> CountAsync(SQLiteAsyncConnection connection, string table)
        => await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table}");

    private static async Task<(string ManuscriptId, string ChapterId)> SeedFullManuscriptAsync(
        SQLiteAsyncConnection connection)
    {
        string manuscriptId = Guid.NewGuid().ToString();
        string chapterId = Guid.NewGuid().ToString();

        await connection.ExecuteAsync(
            "INSERT INTO Manuscript (Id, Title, CreatedAt, ModifiedAt) VALUES (?, 'Winter', 1, 1)", manuscriptId);
        await connection.ExecuteAsync(
            "INSERT INTO Chapter (Id, ManuscriptId, Title, ContentMarkdown, OrderIndex, WordCount, CreatedAt, ModifiedAt) " +
            "VALUES (?, ?, 'One', 'prose', 0, 1, 1, 1)", chapterId, manuscriptId);
        await connection.ExecuteAsync(
            "INSERT INTO InlineImage (Id, ChapterId, Bytes, MimeType, AltText, ByteLength, CreatedAt) VALUES (?, ?, ?, 'image/png', 'a mill', 3, 1)",
            Guid.NewGuid().ToString(), chapterId, new byte[] { 1, 2, 3 });
        await connection.ExecuteAsync(
            "INSERT INTO Character (Id, ManuscriptId, Name, Notes, CreatedAt, ModifiedAt) VALUES (?, ?, 'Elin', '', 1, 1)",
            Guid.NewGuid().ToString(), manuscriptId);
        await connection.ExecuteAsync(
            "INSERT INTO PlotThread (Id, ManuscriptId, Title, Notes, CreatedAt, ModifiedAt) VALUES (?, ?, 'The mill', '', 1, 1)",
            Guid.NewGuid().ToString(), manuscriptId);
        await connection.ExecuteAsync(
            "INSERT INTO DailyGoal (Id, ManuscriptId, TargetWords, IsActive, CreatedAt, ModifiedAt) VALUES (?, ?, 500, 1, 1, 1)",
            Guid.NewGuid().ToString(), manuscriptId);
        await connection.ExecuteAsync(
            "INSERT INTO DailyWritingRecord (Id, ManuscriptId, Date, WordsWritten, GoalTarget, GoalMet) VALUES (?, ?, '2026-03-14', 200, 500, 0)",
            Guid.NewGuid().ToString(), manuscriptId);

        return (manuscriptId, chapterId);
    }
}
