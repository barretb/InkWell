using InkWell.Presentation.Services;

namespace InkWell.Maui.UiTests;

public class DebouncerLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Flush_and_dispose_wait_for_an_action_already_running(bool dispose)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(10));
        bool written = false;
        debouncer.Schedule(async () => { started.TrySetResult(); await release.Task; written = true; });
        await started.Task.WaitAsync(Timeout);
        Task draining = dispose ? debouncer.DisposeAsync().AsTask() : debouncer.FlushAsync();
        try
        {
            Assert.True(debouncer.HasPendingWork);
            Assert.False(draining.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await draining.WaitAsync(Timeout);
        Assert.True(written);
        Assert.False(debouncer.HasPendingWork);
    }

    [Fact]
    public async Task Continuous_changes_save_without_waiting_for_a_pause()
    {
        await using var debouncer = new Debouncer(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        Task typing = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                debouncer.Schedule(() => { saved.TrySetResult(); return Task.CompletedTask; });
                await Task.Delay(10);
            }
        });
        try { await saved.Task.WaitAsync(Timeout); }
        finally { stop.Cancel(); await typing; }
    }

    [Fact]
    public async Task Failed_action_can_be_retried_without_another_edit()
    {
        await using var debouncer = new Debouncer(TimeSpan.FromMinutes(1));
        int attempts = 0;
        int failures = 0;
        debouncer.Failed += (_, _) => failures++;
        debouncer.Schedule(() =>
        {
            if (++attempts == 1)
            {
                throw new IOException("temporary failure");
            }
            return Task.CompletedTask;
        });
        await debouncer.FlushAsync();
        Assert.True(debouncer.HasPendingWork);
        Assert.Equal(1, failures);
        await debouncer.FlushAsync();
        Assert.Equal(2, attempts);
        Assert.False(debouncer.HasPendingWork);
    }

    [Fact]
    public async Task Writes_are_serialized_and_a_failed_old_action_does_not_replace_new_work()
    {
        await using var debouncer = new Debouncer(TimeSpan.FromMinutes(1));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        debouncer.Schedule(async () =>
        {
            started.TrySetResult();
            await release.Task;
            throw new IOException("old write failed");
        });
        Task first = debouncer.FlushAsync();
        await started.Task.WaitAsync(Timeout);
        int writes = 0;
        debouncer.Schedule(() => { writes++; return Task.CompletedTask; });
        Task second = debouncer.FlushAsync();
        try { Assert.Equal(0, writes); }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, second).WaitAsync(Timeout);
        await debouncer.FlushAsync();
        Assert.Equal(1, writes);
        Assert.False(debouncer.HasPendingWork);
    }
}
