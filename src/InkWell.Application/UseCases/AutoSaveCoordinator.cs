using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Services;

namespace InkWell.Application.UseCases;

/// <summary>Tuning for <see cref="AutoSaveCoordinator"/>.</summary>
/// <param name="DebounceInterval">
/// How long typing must pause before a commit. Long enough that a fast typist does not cause a
/// write per keystroke; short enough that an unexpected shutdown costs at most a sentence
/// (FR-004, SC-003).
/// </param>
public sealed record AutoSaveOptions(TimeSpan DebounceInterval)
{
    /// <summary>The shipping default: commit after one second of stillness.</summary>
    public static AutoSaveOptions Default { get; } = new(TimeSpan.FromSeconds(1));
}

/// <summary>
/// Turns a stream of editor keystrokes into durable commits without ever blocking the writer.
/// </summary>
/// <remarks>
/// <para>
/// The editor reports content on every change. Writing on every change would be one transaction per
/// keystroke; writing only on close would risk the whole session. So edits are held in memory and
/// committed once typing pauses for <see cref="AutoSaveOptions.DebounceInterval"/> — and committed
/// immediately on <see cref="FlushAsync"/>, which the host calls on focus loss, chapter switch,
/// distraction-free toggle, and app suspend.
/// </para>
/// <para>
/// Two invariants matter here. The word count is always recomputed from the markdown by
/// <see cref="ProseWordCounter"/> and never taken from the editor, because the count drives the
/// writer's daily goal (FR-009). And a pending edit is never dropped: a newer edit replaces the
/// pending one, and a flush always writes the newest content it has.
/// </para>
/// </remarks>
public sealed class AutoSaveCoordinator : IAsyncDisposable
{
    private readonly IChapterRepository _chapters;
    private readonly IClock _clock;
    private readonly AutoSaveOptions _options;
    private readonly SemaphoreSlim _commitGate = new(1, 1);
    private readonly object _sync = new();

    private PendingEdit? _pending;
    private CancellationTokenSource? _debounce;
    private bool _disposed;

    /// <summary>Creates the coordinator.</summary>
    /// <param name="chapters">The chapter store that performs the transactional commit.</param>
    /// <param name="clock">Supplies the commit timestamp and the local day the words belong to.</param>
    /// <param name="options">Debounce tuning; defaults to <see cref="AutoSaveOptions.Default"/>.</param>
    public AutoSaveCoordinator(IChapterRepository chapters, IClock clock, AutoSaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentNullException.ThrowIfNull(clock);
        _chapters = chapters;
        _clock = clock;
        _options = options ?? AutoSaveOptions.Default;
    }

    /// <summary>Raised after every successful commit, so the editor can refresh its live counts.</summary>
    public event EventHandler<AutoSaveResult>? Saved;

    /// <summary>Raised when a commit fails, so the writer can be told their work is not saved.</summary>
    public event EventHandler<Exception>? SaveFailed;

    /// <summary>Whether an edit is waiting to be committed.</summary>
    public bool HasPendingEdit
    {
        get
        {
            lock (_sync)
            {
                return _pending is not null;
            }
        }
    }

    /// <summary>
    /// Records the editor's current content and restarts the debounce window.
    /// </summary>
    /// <param name="chapterId">The chapter being edited.</param>
    /// <param name="markdown">The editor's full markdown buffer.</param>
    public void QueueEdit(Guid chapterId, string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ObjectDisposedException.ThrowIf(_disposed, this);

        CancellationTokenSource cts;
        lock (_sync)
        {
            // Switching chapters with an edit still pending would otherwise lose it.
            if (_pending is { } previous && previous.ChapterId != chapterId)
            {
                _ = CommitAsync(previous, CancellationToken.None);
            }

            _pending = new PendingEdit(chapterId, markdown);

            _debounce?.Cancel();
            _debounce?.Dispose();
            cts = new CancellationTokenSource();
            _debounce = cts;
        }

        _ = DebounceThenCommitAsync(cts.Token);
    }

    /// <summary>
    /// Commits any pending edit immediately.
    /// </summary>
    /// <returns>The refreshed counts, or null when there was nothing to save.</returns>
    public async Task<AutoSaveResult?> FlushAsync(CancellationToken cancellationToken = default)
    {
        PendingEdit? pending;
        lock (_sync)
        {
            _debounce?.Cancel();
            pending = _pending;
            _pending = null;
        }

        return pending is null ? null : await CommitAsync(pending, cancellationToken).ConfigureAwait(false);
    }

    private async Task DebounceThenCommitAsync(CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(_options.DebounceInterval, debounceToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke or by an explicit flush; that call will commit.
            return;
        }

        PendingEdit? pending;
        lock (_sync)
        {
            pending = _pending;
            _pending = null;
        }

        if (pending is not null)
        {
            await CommitAsync(pending, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<AutoSaveResult?> CommitAsync(PendingEdit pending, CancellationToken cancellationToken)
    {
        // Serialised so two commits can never interleave and produce a stale word count.
        await _commitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var commit = new AutoSaveCommit(
                pending.ChapterId,
                pending.Markdown,
                ProseWordCounter.Count(pending.Markdown),
                _clock.Now,
                _clock.Today);

            AutoSaveResult? result = await _chapters.CommitAutoSaveAsync(commit, cancellationToken).ConfigureAwait(false);

            if (result is not null)
            {
                Saved?.Invoke(this, result);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed autosave must reach the writer rather than disappear into a fire-and-forget
            // task; losing work silently is the one outcome FR-004 exists to prevent.
            SaveFailed?.Invoke(this, ex);
            return null;
        }
        finally
        {
            _commitGate.Release();
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

        // Anything still pending is committed before shutdown rather than discarded.
        await FlushAsync().ConfigureAwait(false);

        lock (_sync)
        {
            _debounce?.Dispose();
            _debounce = null;
        }

        _commitGate.Dispose();
    }

    private sealed record PendingEdit(Guid ChapterId, string Markdown);
}
