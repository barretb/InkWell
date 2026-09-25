using InkWell.Infrastructure.Persistence;
using InkWell.Infrastructure.Tests.Fixtures;

namespace InkWell.Infrastructure.Tests.Persistence;

public class ConnectionLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_or_deleting_waits_for_the_complete_repository_operation(bool delete)
    {
        await using var fixture = new KeyedDatabaseFixture();
        SqliteConnectionLease lease = await fixture.Factory.AcquireConnectionAsync();
        Task lifecycle = delete ? fixture.Factory.DeleteDatabaseAsync() : fixture.Factory.CloseAsync();
        try
        {
            Assert.False(lifecycle.IsCompleted);
            // The connection remains usable until its owner finishes, even though closing/reset
            // was requested before this SQL statement was submitted.
            Assert.Equal(1, await lease.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        }
        finally { lease.Dispose(); }
        await lifecycle.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.RestartAsync();
        using SqliteConnectionLease reopened = await fixture.Factory.AcquireConnectionAsync();
        Assert.Equal(0, await reopened.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Manuscript"));
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_reset_keeps_the_original_store_and_key()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SqliteConnectionLease lease = await fixture.Factory.AcquireConnectionAsync();
        string key = await fixture.KeyStore.GetOrCreateKeyAsync();
        using var canceled = new CancellationTokenSource();
        Task reset = fixture.Factory.DeleteDatabaseAsync(canceled.Token);
        canceled.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reset); }
        finally { lease.Dispose(); }
        Assert.True(File.Exists(fixture.DatabasePath));
        Assert.Equal(key, await fixture.KeyStore.GetOrCreateKeyAsync());
        await fixture.RestartAsync();
    }
    [Fact]
    public async Task Disposal_waits_for_active_work_and_rejects_later_operations()
    {
        await using var fixture = new KeyedDatabaseFixture();
        SqliteConnectionLease lease = await fixture.Factory.AcquireConnectionAsync();
        Task disposal = fixture.Factory.DisposeAsync().AsTask();
        try
        {
            Assert.False(disposal.IsCompleted);
            Assert.Equal(1, await lease.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        }
        finally { lease.Dispose(); }
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Factory.AcquireConnectionAsync());
        await fixture.Factory.DisposeAsync();
    }
}
