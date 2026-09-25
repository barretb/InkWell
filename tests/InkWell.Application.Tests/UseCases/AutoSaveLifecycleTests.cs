using System.Collections.Concurrent;
using InkWell.Application.Tests.Fakes;
using InkWell.Application.UseCases;
using InkWell.Domain.Entities;

namespace InkWell.Application.Tests.UseCases;

public class AutoSaveLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static AutoSaveOptions Slow => new(TimeSpan.FromMinutes(1))
    {
        MaximumSaveInterval = TimeSpan.FromMinutes(1),
    };

    private static async Task<(FakeChapterRepository Store, Guid Id)> CreateAsync()
    {
        var store = new FakeChapterRepository();
        var chapter = new Chapter { Id = Guid.NewGuid(), ManuscriptId = Guid.NewGuid(), Title = "Draft" };
        await store.AddAsync(chapter);
        return (store, chapter.Id);
    }

    [Fact]
    public async Task Continuous_typing_saves_before_the_pause_interval()
    {
        var (store, id) = await CreateAsync();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var saver = new AutoSaveCoordinator(store, new FixedClock(), new(TimeSpan.FromSeconds(30))
        {
            MaximumSaveInterval = TimeSpan.FromMilliseconds(100),
        });
        saver.Saved += (_, _) => saved.TrySetResult();
        using var stop = new CancellationTokenSource();
        Task typing = Task.Run(async () =>
        {
            int edit = 0;
            while (!stop.IsCancellationRequested)
            {
                saver.QueueEdit(id, $"continuous words {edit++}");
                await Task.Delay(10);
            }
        });
        try { await saved.Task.WaitAsync(Timeout); }
        finally { stop.Cancel(); await typing; }
        await saver.FlushAsync();
        Assert.StartsWith("continuous words", store.Peek(id)!.ContentMarkdown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Flush_and_dispose_wait_for_a_write_already_started(bool dispose)
    {
        var (store, id) = await CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.BeforeCommit = async _ => { started.TrySetResult(); await release.Task; };
        await using var saver = new AutoSaveCoordinator(store, new FixedClock(), new(TimeSpan.FromMilliseconds(10)));
        saver.QueueEdit(id, "must reach storage");
        await started.Task.WaitAsync(Timeout);
        Task draining = dispose ? saver.DisposeAsync().AsTask() : saver.FlushAsync();
        try
        {
            Assert.True(saver.HasPendingEdit);
            Assert.False(draining.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await draining.WaitAsync(Timeout);
        Assert.Equal("must reach storage", store.Peek(id)!.ContentMarkdown);
        Assert.False(saver.HasPendingEdit);
    }

    [Fact]
    public async Task Failed_edit_is_retained_and_can_be_retried_without_more_typing()
    {
        var (store, id) = await CreateAsync();
        store.BeforeCommit = _ => throw new IOException("temporary write failure");
        await using var saver = new AutoSaveCoordinator(store, new FixedClock(), Slow);
        int failures = 0;
        saver.SaveFailed += (_, _) => failures++;
        saver.QueueEdit(id, "keep this buffer");
        Assert.Null(await saver.FlushAsync());
        Assert.True(saver.HasPendingEdit);
        Assert.Equal(1, failures);
        store.BeforeCommit = null;
        await saver.FlushAsync();
        Assert.Equal("keep this buffer", store.Peek(id)!.ContentMarkdown);
        Assert.False(saver.HasPendingEdit);
    }

    [Fact]
    public async Task An_older_failed_write_cannot_replace_a_newer_queued_edit()
    {
        var (store, id) = await CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = new ConcurrentQueue<string>();
        store.BeforeCommit = async commit =>
        {
            if (commit.ContentMarkdown == "old")
            {
                started.TrySetResult();
                await release.Task;
                throw new IOException("old write failed");
            }
            committed.Enqueue(commit.ContentMarkdown);
        };
        await using var saver = new AutoSaveCoordinator(store, new FixedClock(), Slow);
        saver.QueueEdit(id, "old");
        Task first = saver.FlushAsync();
        await started.Task.WaitAsync(Timeout);
        saver.QueueEdit(id, "new");
        Task second = saver.FlushAsync();
        release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);
        await saver.FlushAsync();
        Assert.Equal("new", Assert.Single(committed));
        Assert.Equal("new", store.Peek(id)!.ContentMarkdown);
        Assert.False(saver.HasPendingEdit);
    }

    [Fact]
    public async Task Canceling_a_flush_wait_does_not_cancel_or_discard_the_write()
    {
        var (store, id) = await CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.BeforeCommit = async _ => { started.TrySetResult(); await release.Task; };
        await using var saver = new AutoSaveCoordinator(store, new FixedClock(), Slow);
        saver.QueueEdit(id, "survive cancellation");
        using var canceled = new CancellationTokenSource();
        Task wait = saver.FlushAsync(canceled.Token);
        await started.Task.WaitAsync(Timeout);
        canceled.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait); }
        finally { release.TrySetResult(); }
        await saver.FlushAsync();
        Assert.Equal("survive cancellation", store.Peek(id)!.ContentMarkdown);
    }
}
