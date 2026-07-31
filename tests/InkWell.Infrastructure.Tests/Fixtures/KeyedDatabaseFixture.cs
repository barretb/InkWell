using InkWell.Application.Abstractions;
using InkWell.Application.Tests.Fakes;
using InkWell.Infrastructure.Persistence;
using InkWell.Infrastructure.Security;

namespace InkWell.Infrastructure.Tests.Fixtures;

/// <summary>
/// Points the whole persistence stack at a throwaway directory.
/// </summary>
public sealed class TempAppStoragePaths : IAppStoragePaths, IDisposable
{
    private readonly string _directory;

    /// <summary>Creates a unique temporary directory for one test.</summary>
    public TempAppStoragePaths()
    {
        _directory = Path.Combine(Path.GetTempPath(), "inkwell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    /// <inheritdoc />
    public string DatabaseFilePath => Path.Combine(_directory, "inkwell.db3");

    /// <summary>Deletes the directory and everything in it, including WAL side files.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A lingering file handle must never fail a test run; the OS reclaims the temp folder.
        }
    }
}

/// <summary>
/// A real SQLCipher database in a temporary directory, keyed from an in-memory secure store.
/// </summary>
/// <remarks>
/// Integration tests run against the real cipher rather than a plaintext SQLite file, because the
/// things most worth testing here — that the file is unreadable without the key, that cascade
/// deletes leave nothing behind, that a reopen sees committed autosaves — only mean something if
/// the encryption is genuinely in the path (research.md §2).
/// </remarks>
public sealed class KeyedDatabaseFixture : IAsyncDisposable
{
    private readonly TempAppStoragePaths _paths = new();
    private readonly InMemorySecureStore _secureStore = new();
    private SqlCipherConnectionFactory _factory;

    /// <summary>Creates the fixture with a freshly generated database key.</summary>
    public KeyedDatabaseFixture()
    {
        KeyStore = new KeyStore(_secureStore);
        _factory = new SqlCipherConnectionFactory(KeyStore, _paths);
    }

    /// <summary>The key store backing this database.</summary>
    public IKeyStore KeyStore { get; private set; }

    /// <summary>The connection factory under test.</summary>
    public ISqliteConnectionFactory Factory => _factory;

    /// <summary>Where the encrypted database file lives.</summary>
    public string DatabasePath => _paths.DatabaseFilePath;

    /// <summary>The paths this fixture hands to the stack.</summary>
    public IAppStoragePaths Paths => _paths;

    /// <summary>
    /// Closes and reopens the database with the same key — the test equivalent of the writer
    /// quitting the app and starting it again (US1 scenario 3).
    /// </summary>
    public async Task RestartAsync()
    {
        await _factory.CheckpointAsync().ConfigureAwait(false);
        await _factory.DisposeAsync().ConfigureAwait(false);
        _factory = new SqlCipherConnectionFactory(KeyStore, _paths);
        await _factory.GetConnectionAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the raw database file while SQLite still holds it open.
    /// </summary>
    /// <remarks>
    /// On Windows the database file cannot be opened with the default share mode while a
    /// connection is live, so the "is anything readable in here?" privacy checks have to ask for
    /// shared access explicitly rather than closing the database first — closing it would let the
    /// test pass against a file that was only unreadable because it had been tidied up.
    /// </remarks>
    public async Task<byte[]> ReadDatabaseBytesAsync()
    {
        await _factory.CheckpointAsync().ConfigureAwait(false);

        await using var stream = new FileStream(
            DatabasePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes).ConfigureAwait(false);
        return bytes;
    }

    /// <summary>
    /// Closes the database and reopens it with a brand-new key, to prove the existing file cannot
    /// be read without the original one.
    /// </summary>
    public async Task<ISqliteConnectionFactory> ReopenWithDifferentKeyAsync()
    {
        await _factory.DisposeAsync().ConfigureAwait(false);
        var otherKeyStore = new FakeKeyStore("00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF");
        return new SqlCipherConnectionFactory(otherKeyStore, _paths);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync().ConfigureAwait(false);
        _paths.Dispose();
    }
}
