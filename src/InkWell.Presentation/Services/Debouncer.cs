namespace InkWell.Presentation.Services;

/// <summary>
/// Runs an action once the writer stops changing something.
/// </summary>
/// <remarks>
/// <para>
/// InkWell has no save buttons anywhere: chapters, character notes, plot threads, and the daily
/// goal all persist themselves (FR-004). Chapter prose gets its own coordinator because it also has
/// to recompute word counts and touch the day's writing record transactionally. Everything else
/// shares this: hold the latest edit, commit it once typing pauses, and always commit on the way
/// out so navigating away can never be the thing that loses a change.
/// </para>
/// <para>
/// Only the newest scheduled action survives — an older pending edit to the same field is
/// superseded, not queued, so a fast typist causes one write rather than one per keystroke.
/// </para>
/// </remarks>
public sealed class Debouncer : IAsyncDisposable
{
    private readonly TimeSpan _interval;
    private readonly object _sync = new();

    private Func<Task>? _pending;
    private CancellationTokenSource? _timer;
    private bool _disposed;

    /// <summary>Creates a debouncer.</summary>
    /// <param name="interval">How long changes must pause before the action runs.</param>
    public Debouncer(TimeSpan? interval = null) => _interval = interval ?? TimeSpan.FromMilliseconds(600);

    /// <summary>Whether an edit is waiting to be written.</summary>
    public bool HasPendingWork
    {
        get
        {
            lock (_sync)
            {
                return _pending is not null;
            }
        }
    }

    /// <summary>Raised when a debounced action throws, so the writer can be told.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>Replaces any pending action and restarts the pause timer.</summary>
    public void Schedule(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(_disposed, this);

        CancellationTokenSource cts;
        lock (_sync)
        {
            _pending = action;
            _timer?.Cancel();
            _timer?.Dispose();
            cts = new CancellationTokenSource();
            _timer = cts;
        }

        _ = RunAfterPauseAsync(cts.Token);
    }

    /// <summary>Runs any pending action immediately.</summary>
    public async Task FlushAsync()
    {
        Func<Task>? action;
        lock (_sync)
        {
            _timer?.Cancel();
            action = _pending;
            _pending = null;
        }

        if (action is not null)
        {
            await InvokeAsync(action).ConfigureAwait(false);
        }
    }

    private async Task RunAfterPauseAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_interval, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit or by an explicit flush; that call will run it.
            return;
        }

        Func<Task>? action;
        lock (_sync)
        {
            action = _pending;
            _pending = null;
        }

        if (action is not null)
        {
            await InvokeAsync(action).ConfigureAwait(false);
        }
    }

    private async Task InvokeAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed background save must not vanish into a fire-and-forget task.
            Failed?.Invoke(this, ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await FlushAsync().ConfigureAwait(false);

        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
