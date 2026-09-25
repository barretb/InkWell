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

    /// <summary>Holds the store open for an operation, excluding reset and close until disposed.</summary>
    Task<SqliteConnectionLease> AcquireConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes the write-ahead log into the main database file. Called on app suspend and close so
    /// that an un-checkpointed WAL can never look like lost work (research.md §2).
    /// </summary>
    Task CheckpointAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the connection so the database file can be deleted or replaced.</summary>
    Task CloseAsync();

    /// <summary>Closes and removes the store and its sidecars before deleting its encryption key.</summary>
    Task DeleteDatabaseAsync(CancellationToken cancellationToken = default);
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
        using SqliteConnectionLease lease = await AcquireConnectionAsync(cancellationToken).ConfigureAwait(false);
        return lease.Connection;
    }

    /// <inheritdoc />
    public async Task<SqliteConnectionLease> AcquireConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SQLiteAsyncConnection connection = await OpenCoreAsync(cancellationToken).ConfigureAwait(false);
            return new SqliteConnectionLease(connection, () => _gate.Release());
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    // Caller holds _gate until its complete repository operation has finished.
    private async Task<SQLiteAsyncConnection> OpenCoreAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        try
        {
            await DatabaseMigrator.MigrateAsync(opened, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await opened.CloseAsync().ConfigureAwait(false);
            throw;
        }

        _connection = opened;
        return opened;
    }

    /// <inheritdoc />
    public async Task CheckpointAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is not null)
            {
                await _connection.ExecuteScalarAsync<string>("PRAGMA wal_checkpoint(TRUNCATE)").ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
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
    public async Task DeleteDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            // Once deletion starts, finish the file/key transition without cancellation. No new
            // connection may open with either key halfway through this operation.
            if (_connection is not null)
            {
                await _connection.RunInTransactionAsync(connection =>
                {
                    foreach (string table in DatabaseMigrator.TableNames.Reverse())
                    {
                        connection.Execute($"DELETE FROM {table}");
                    }
                }).ConfigureAwait(false);
                await _connection.CloseAsync().ConfigureAwait(false);
                _connection = null;
            }

            string path = _paths.DatabaseFilePath;
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
            File.Delete(path + "-journal");
            File.Delete(path);
            // Keep the old key if file deletion fails. Never leave a surviving encrypted file
            // paired with a newly generated key. The next open creates a fresh database/key.
            await _keyStore.DeleteKeyAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            if (_connection is not null)
            {
                await _connection.CloseAsync().ConfigureAwait(false);
                _connection = null;
            }
            _disposed = true;
        }
        finally
        {
            // Do not dispose the semaphore: queued callers must acquire it and observe _disposed,
            // rather than race disposal or remain stuck. No semaphore wait handle is allocated.
            _gate.Release();
        }
    }
}
