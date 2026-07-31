using InkWell.Application.Abstractions;
using InkWell.Application.Tests.Fakes;
using InkWell.Infrastructure.Persistence;
using InkWell.Infrastructure.Security;
using InkWell.Infrastructure.Tests.Fixtures;
using SQLite;

namespace InkWell.Infrastructure.Tests.Persistence;

/// <summary>
/// FR-016 and the durability settings behind FR-004 / SC-003, verified against a real keyed
/// SQLCipher file rather than a mock.
/// </summary>
public class EncryptedConnectionTests
{
    [Fact]
    public async Task Opens_with_the_stored_key()
    {
        await using var fixture = new KeyedDatabaseFixture();

        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();

        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT 1"));
        Assert.True(File.Exists(fixture.DatabasePath));
    }

    [Fact]
    public async Task Applies_the_durability_and_integrity_pragmas()
    {
        await using var fixture = new KeyedDatabaseFixture();

        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();

        string journalMode = await connection.ExecuteScalarAsync<string>("PRAGMA journal_mode");
        int synchronous = await connection.ExecuteScalarAsync<int>("PRAGMA synchronous");
        int foreignKeys = await connection.ExecuteScalarAsync<int>("PRAGMA foreign_keys");

        Assert.Equal("wal", journalMode, ignoreCase: true);
        Assert.Equal(1, synchronous); // 1 == NORMAL
        Assert.Equal(1, foreignKeys);
    }

    [Fact]
    public async Task The_same_key_reopens_the_same_data()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO Manuscript (Id, Title, CreatedAt, ModifiedAt) VALUES (?, ?, ?, ?)",
            Guid.NewGuid().ToString(), "The Long Winter", 1L, 1L);

        await fixture.RestartAsync();

        SQLiteAsyncConnection reopened = await fixture.Factory.GetConnectionAsync();
        string title = await reopened.ExecuteScalarAsync<string>("SELECT Title FROM Manuscript");
        Assert.Equal("The Long Winter", title);
    }

    [Fact]
    public async Task A_different_key_cannot_open_the_database()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO Manuscript (Id, Title, CreatedAt, ModifiedAt) VALUES (?, ?, ?, ?)",
            Guid.NewGuid().ToString(), "The Long Winter", 1L, 1L);
        await fixture.Factory.CheckpointAsync();

        ISqliteConnectionFactory wrongKey = await fixture.ReopenWithDifferentKeyAsync();

        await Assert.ThrowsAnyAsync<SQLiteException>(async () =>
        {
            SQLiteAsyncConnection wrong = await wrongKey.GetConnectionAsync();
            await wrong.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Manuscript");
        });

        await wrongKey.DisposeAsync();
    }

    [Fact]
    public async Task SQLCipher_is_the_active_provider()
    {
        // If this ever reports nothing, the app is running on plain SQLite and every other
        // encryption guarantee in the spec is silently void (research.md §2).
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();

        string cipherVersion = await connection.ExecuteScalarAsync<string>("PRAGMA cipher_version");

        Assert.False(string.IsNullOrWhiteSpace(cipherVersion));
    }

    [Fact]
    public async Task Prose_and_image_bytes_are_not_readable_in_the_raw_database_file()
    {
        // FR-016: the file on disk must not leak the writer's words or their embedded images.
        const string secretProse = "SnowfellForNineDaysWithoutPause";
        byte[] secretImageBytes = System.Text.Encoding.ASCII.GetBytes("PNGDATA-SECRET-IMAGE-CONTENT");

        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();
        string manuscriptId = Guid.NewGuid().ToString();
        string chapterId = Guid.NewGuid().ToString();
        await connection.ExecuteAsync(
            "INSERT INTO Manuscript (Id, Title, CreatedAt, ModifiedAt) VALUES (?, ?, ?, ?)",
            manuscriptId, "Winter", 1L, 1L);
        await connection.ExecuteAsync(
            "INSERT INTO Chapter (Id, ManuscriptId, Title, ContentMarkdown, OrderIndex, WordCount, CreatedAt, ModifiedAt) " +
            "VALUES (?, ?, ?, ?, 0, 1, 1, 1)",
            chapterId, manuscriptId, "One", secretProse);
        await connection.ExecuteAsync(
            "INSERT INTO InlineImage (Id, ChapterId, Bytes, MimeType, AltText, ByteLength, CreatedAt) " +
            "VALUES (?, ?, ?, 'image/png', NULL, ?, 1)",
            Guid.NewGuid().ToString(), chapterId, secretImageBytes, secretImageBytes.Length);

        byte[] raw = await fixture.ReadDatabaseBytesAsync();
        string asText = System.Text.Encoding.UTF8.GetString(raw);

        Assert.DoesNotContain(secretProse, asText, StringComparison.Ordinal);
        Assert.DoesNotContain("PNGDATA-SECRET-IMAGE-CONTENT", asText, StringComparison.Ordinal);

        // A plain SQLite database begins with this magic string; an encrypted one does not.
        Assert.DoesNotContain("SQLite format 3", asText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checkpoint_folds_the_write_ahead_log_into_the_database()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SQLiteAsyncConnection connection = await fixture.Factory.GetConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO Manuscript (Id, Title, CreatedAt, ModifiedAt) VALUES (?, ?, ?, ?)",
            Guid.NewGuid().ToString(), "Winter", 1L, 1L);

        await fixture.Factory.CheckpointAsync();

        string walPath = fixture.DatabasePath + "-wal";
        Assert.True(!File.Exists(walPath) || new FileInfo(walPath).Length == 0);
    }

    [Fact]
    public async Task A_key_store_failure_is_reported_as_key_store_unavailable()
    {
        // The usual real-world cause is a missing Keychain entitlement on iOS/Mac Catalyst; the app
        // must be able to tell the writer that rather than crash (research.md §2).
        var secureStore = new InMemorySecureStore { SimulateUnavailable = true };
        var keyStore = new KeyStore(secureStore);
        using var paths = new TempAppStoragePaths();
        await using var factory = new SqlCipherConnectionFactory(keyStore, paths);

        await Assert.ThrowsAsync<KeyStoreUnavailableException>(() => factory.GetConnectionAsync());
    }

    [Fact]
    public async Task The_key_is_generated_once_and_reused()
    {
        var secureStore = new InMemorySecureStore();
        var keyStore = new KeyStore(secureStore);

        string first = await keyStore.GetOrCreateKeyAsync();
        string second = await keyStore.GetOrCreateKeyAsync();

        Assert.Equal(first, second);
        Assert.Equal(1, secureStore.Count);
        Assert.Equal(64, first.Length); // 32 random bytes, hex encoded
    }

    [Fact]
    public async Task Deleting_the_key_removes_it_from_secure_storage()
    {
        var secureStore = new InMemorySecureStore();
        var keyStore = new KeyStore(secureStore);
        await keyStore.GetOrCreateKeyAsync();

        await keyStore.DeleteKeyAsync();

        Assert.Equal(0, secureStore.Count);
        Assert.Null(await secureStore.GetAsync(KeyStore.SecureStoreKey));
    }
}
