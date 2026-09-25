using System.Diagnostics;
using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Services;

namespace InkWell.Application.UseCases;

/// <summary>Autosave debounce and maximum time before submitting dirty content.</summary>
/// <param name="DebounceInterval">How long typing must pause before a commit.</param>
public sealed record AutoSaveOptions(TimeSpan DebounceInterval)
{
    /// <summary>Maximum dirty age, even when typing never pauses. Storage latency is additional.</summary>
    public TimeSpan MaximumSaveInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Save after one second of stillness, or three seconds of continuous edits.</summary>
    public static AutoSaveOptions Default { get; } = new(TimeSpan.FromSeconds(1));
}

/// <summary>Serializes durable chapter saves, retaining failed edits for a later retry.</summary>
public sealed class AutoSaveCoordinator : IAsyncDisposable
{
    private readonly IChapterRepository _chapters;
    private readonly IClock _clock;
    private readonly AutoSaveOptions _options;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, PendingEdit> _pending = [];
    private readonly Dictionary<Guid, long> _versions = [];
    private Task<AutoSaveResult?> _tail = Task.FromResult<AutoSaveResult?>(null);
    private CancellationTokenSource? _timer;
    private long _dirtySince;
    private long _version;
    private bool _disposed;

    /// <summary>Creates a coordinator using the supplied store, clock and save intervals.</summary>
    public AutoSaveCoordinator(IChapterRepository chapters, IClock clock, AutoSaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentNullException.ThrowIfNull(clock);
        _chapters = chapters;
        _clock = clock;
        _options = options ?? AutoSaveOptions.Default;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.DebounceInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.MaximumSaveInterval, TimeSpan.Zero);
    }

    /// <summary>Raised after a successful commit with refreshed counts.</summary>
    public event EventHandler<AutoSaveResult>? Saved;

    /// <summary>Raised when persistence fails; the latest failed edit remains available to retry.</summary>
    public event EventHandler<Exception>? SaveFailed;

    /// <summary>Whether there is unsaved content, including a write already in progress.</summary>
    public bool HasPendingEdit
    {
        get
        {
            lock (_sync)
            {
                return _pending.Count > 0 || !_tail.IsCompleted;
            }
        }
    }

    /// <summary>Records the latest content without postponing saving indefinitely.</summary>
    public void QueueEdit(Guid chapterId, string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending.Count == 0)
            {
                _dirtySince = Stopwatch.GetTimestamp();
            }

            long version = ++_version;
            _versions[chapterId] = version;
            _pending[chapterId] = new PendingEdit(chapterId, markdown, version);
            _timer?.Cancel();
            var timer = new CancellationTokenSource();
            _timer = timer;
            TimeSpan remaining = _options.MaximumSaveInterval - Stopwatch.GetElapsedTime(_dirtySince);
            TimeSpan delay = remaining < _options.DebounceInterval ? remaining : _options.DebounceInterval;
            _ = RunTimerAsync(timer, delay > TimeSpan.Zero ? delay : TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Submits pending edits and waits for all previously submitted writes. Cancellation stops
    /// waiting; it does not discard or cancel a durable save already submitted.
    /// </summary>
    public Task<AutoSaveResult?> FlushAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _timer?.Cancel();
            _timer = null;
            bool hadWork = _pending.Count > 0 || !_tail.IsCompleted;
            SubmitPendingLocked();
            return hadWork ? _tail.WaitAsync(cancellationToken) : Task.FromResult<AutoSaveResult?>(null);
        }
    }

    private async Task RunTimerAsync(CancellationTokenSource timer, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, timer.Token).ConfigureAwait(false);
            lock (_sync)
            {
                // A canceled timer may already have completed its delay. It must not steal a
                // newer timer's work, especially during flush or disposal.
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

    // Called only under _sync. Register the whole batch before any writer can flush or dispose.
    private void SubmitPendingLocked()
    {
        if (_pending.Count == 0)
        {
            return;
        }
        PendingEdit[] batch = [.. _pending.Values];
        _pending.Clear();
        Task<AutoSaveResult?> previous = _tail;
        _tail = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            AutoSaveResult? last = null;
            foreach (PendingEdit edit in batch)
            {
                try
                {
                    var commit = new AutoSaveCommit(edit.ChapterId, edit.Markdown,
                        ProseWordCounter.Count(edit.Markdown), _clock.Now, _clock.Today);
                    last = await _chapters.CommitAutoSaveAsync(commit).ConfigureAwait(false);
                    if (last is not null)
                    {
                        Saved?.Invoke(this, last);
                    }
                }
                catch (Exception ex)
                {
                    lock (_sync)
                    {
                        // Never restore an older buffer over an edit already pending or queued.
                        if (_versions[edit.ChapterId] == edit.Version)
                        {
                            if (_pending.Count == 0)
                            {
                                _dirtySince = Stopwatch.GetTimestamp();
                            }
                            _pending[edit.ChapterId] = edit;
                        }
                    }
                    SaveFailed?.Invoke(this, ex);
                }
            }
            return last;
        });
    }

    /// <summary>Rejects new edits and waits for pending and in-flight saves before returning.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
            return new ValueTask(FlushAsync());
        }
    }

    private sealed record PendingEdit(Guid ChapterId, string Markdown, long Version);
}
