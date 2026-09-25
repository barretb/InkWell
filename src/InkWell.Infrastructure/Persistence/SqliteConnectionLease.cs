using SQLite;

namespace InkWell.Infrastructure.Persistence;

/// <summary>Keeps a repository operation exclusive with connection closing and store deletion.</summary>
public sealed class SqliteConnectionLease : IDisposable
{
    private Action? _release;

    internal SqliteConnectionLease(SQLiteAsyncConnection connection, Action release)
    {
        Connection = connection;
        _release = release;
    }

    /// <summary>The connection; use only until this lease is disposed.</summary>
    public SQLiteAsyncConnection Connection { get; }

    /// <summary>Allows the next repository or lifecycle operation to proceed.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
