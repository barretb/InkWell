using InkWell.Application.Abstractions;
using SQLite;

namespace InkWell.Infrastructure.Persistence;

/// <summary>
/// Opens the single SQLCipher-encrypted database and keeps one connection for the process.
/// </summary>
public interface ISqliteConnectionFactory : IAsyncDisposable
{
    /// <summary>
    /// Returns the shared connection, opening the database and applying the schema on first use.
    /// </summary>
    /// <exception cref="KeyStoreUnavailableException">The cipher key could not be obtained.</exception>
    Task<SQLiteAsyncConnection> GetConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes the write-ahead log into the main database file. Called on app suspend and close so
    /// that an un-checkpointed WAL can never look like lost work (research.md §2).
    /// </summary>
    Task CheckpointAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the connection so the database file can be deleted or replaced.</summary>
    Task CloseAsync();
}

/// <summary>
/// The SQLCipher connection factory.
/// </summary>
/// <remarks>
/// <para>
/// Three PRAGMA choices carry the app's durability guarantee (FR-004, SC-003):
/// </para>
/// <list type="bullet">
///   <item>
///     <c>journal_mode=WAL</c> — writers do not block readers, so an autosave commit never stalls
///     the UI thread, and a committed transaction survives an app crash.
///   </item>
///   <item>
///     <c>synchronous=NORMAL</c> — under WAL this still survives an application crash (only an OS
///     crash or power loss can drop the last transaction) while avoiding an fsync per commit, which
///     matters because InkWell commits every couple of seconds while the writer types.
///   </item>
///   <item>
///     <c>foreign_keys=ON</c> — SQLite defaults this off, and every cascade delete in the data model
///     depends on it (FR-018, SC-008).
///   </item>
/// </list>
/// <para>
/// The key is passed through sqlite-net's <c>key:</c> parameter, which issues <c>PRAGMA key</c>
/// before any other statement, so the file is never touched unencrypted.
/// </para>
/// </remarks>
public sealed class SqlCipherConnectionFactory : ISqliteConnectionFactory
{
    private const SQLiteOpenFlags Flags =
        SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex;

    private readonly IKeyStore _keyStore;
    private readonly IAppStoragePaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SQLiteAsyncConnection? _connection;
    private bool _disposed;

    static SqlCipherConnectionFactory()
    {
        // Binds SQLitePCLRaw to the bundled e_sqlcipher provider. Safe to call more than once.
        SQLitePCL.Batteries_V2.Init();
    }

    /// <summary>Creates the factory.</summary>
    /// <param name="keyStore">Supplies the database cipher key.</param>
    /// <param name="paths">Supplies the database file location.</param>
    public SqlCipherConnectionFactory(IKeyStore keyStore, IAppStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(paths);
        _keyStore = keyStore;
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<SQLiteAsyncConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not null)
            {
                return _connection;
            }

            string key = await _keyStore.GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false);

            string path = _paths.DatabaseFilePath;
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var connectionString = new SQLiteConnectionString(
                databasePath: path,
                openFlags: Flags,
                storeDateTimeAsTicks: true,
                key: key,
                postKeyAction: connection =>
                {
                    // Every one of these PRAGMAs may answer with a row. sqlite-net's
                    // ExecuteNonQuery treats an unexpected SQLITE_ROW as a failure ("not an
                    // error"), so they are all issued through ExecuteScalar, which is happy with
                    // either a row or none.
                    connection.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
                    connection.ExecuteScalar<string>("PRAGMA synchronous=NORMAL");
                    connection.ExecuteScalar<string>("PRAGMA foreign_keys=ON");
                    connection.ExecuteScalar<string>("PRAGMA busy_timeout=5000");
                });

            var opened = new SQLiteAsyncConnection(connectionString);
            await DatabaseMigrator.MigrateAsync(opened, cancellationToken).ConfigureAwait(false);

            _connection = opened;
            return opened;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task CheckpointAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is null)
        {
            return;
        }

        await _connection.ExecuteScalarAsync<string>("PRAGMA wal_checkpoint(TRUNCATE)").ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CloseAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_connection is null)
            {
                return;
            }

            await _connection.CloseAsync().ConfigureAwait(false);
            _connection = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await CloseAsync().ConfigureAwait(false);
        _disposed = true;
        _gate.Dispose();
    }
}
