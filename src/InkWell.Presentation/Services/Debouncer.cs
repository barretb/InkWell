using System.Diagnostics;

namespace InkWell.Presentation.Services;

/// <summary>Coalesces edits while bounding dirty time and tracking every submitted save.</summary>
public sealed class Debouncer : IAsyncDisposable
{
    private readonly TimeSpan _interval;
    private readonly TimeSpan _maximumInterval;
    private readonly object _sync = new();
    private Func<Task>? _pending;
    private CancellationTokenSource? _timer;
    private Task _tail = Task.CompletedTask;
    private long _dirtySince;
    private long _version;
    private bool _disposed;

    /// <summary>Creates a debouncer with a pause interval and a maximum dirty age.</summary>
    public Debouncer(TimeSpan? interval = null, TimeSpan? maximumInterval = null)
    {
        _interval = interval ?? TimeSpan.FromMilliseconds(600);
        _maximumInterval = maximumInterval ?? TimeSpan.FromSeconds(3);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_maximumInterval, TimeSpan.Zero);
    }

    /// <summary>Whether pending or in-flight work is not yet saved.</summary>
    public bool HasPendingWork
    {
        get
        {
            lock (_sync)
            {
                return _pending is not null || !_tail.IsCompleted;
            }
        }
    }

    /// <summary>Raised when a save fails; the latest action remains available to retry.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>Replaces the pending action without postponing it past the maximum interval.</summary>
    public void Schedule(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending is null)
            {
                _dirtySince = Stopwatch.GetTimestamp();
            }
            _pending = action;
            _version++;
            _timer?.Cancel();
            var timer = new CancellationTokenSource();
            _timer = timer;
            TimeSpan remaining = _maximumInterval - Stopwatch.GetElapsedTime(_dirtySince);
            TimeSpan delay = remaining < _interval ? remaining : _interval;
            _ = RunTimerAsync(timer, delay > TimeSpan.Zero ? delay : TimeSpan.Zero);
        }
    }

    /// <summary>Runs pending work and waits for all previously submitted actions.</summary>
    public Task FlushAsync()
    {
        lock (_sync)
        {
            _timer?.Cancel();
            _timer = null;
            SubmitPendingLocked();
            return _tail;
        }
    }

    private async Task RunTimerAsync(CancellationTokenSource timer, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, timer.Token).ConfigureAwait(false);
            lock (_sync)
            {
                if (_timer != timer || timer.IsCancellationRequested || _disposed)
                {
                    return;
                }
                _timer = null;
                SubmitPendingLocked();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer edit or a flush owns the pending work.
        }
        finally
        {
            lock (_sync)
            {
                if (_timer == timer)
                {
                    _timer = null;
                }
                timer.Dispose();
            }
        }
    }

    private void SubmitPendingLocked()
    {
        if (_pending is not { } action)
        {
            return;
        }
        _pending = null;
        long version = _version;
        Task previous = _tail;
        _tail = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    if (_version == version)
                    {
                        _dirtySince = Stopwatch.GetTimestamp();
                        _pending = action;
                    }
                }
                Failed?.Invoke(this, ex);
            }
        });
    }

    /// <summary>Stops new edits and waits for pending and in-flight work.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
            return new ValueTask(FlushAsync());
        }
    }
}
