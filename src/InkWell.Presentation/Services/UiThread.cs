using Microsoft.Maui.Dispatching;

namespace InkWell.Presentation.Services;

/// <summary>
/// Runs work on the UI thread.
/// </summary>
/// <remarks>
/// <para>
/// Autosave commits, the debounce timers, and the editor bridge's load handshake all resume on
/// thread pool threads, and what they produce ends up on screen: a status line, a word count, an
/// alert. Touching a native view from off the UI thread is a crash on every platform — WinUI answers
/// it with <c>RPC_E_WRONG_THREAD</c> (0x8001010E) out of the <c>ContentDialog</c> constructor — and
/// because these paths are reached from <c>async void</c> event handlers, that exception surfaces as
/// an unhandled one on the thread pool and takes the whole app down, which costs the writer exactly
/// the unsaved paragraph the failing save was trying to tell them about.
/// </para>
/// <para>
/// Work runs inline when the caller is already on the UI thread, so a flush from a button or a page
/// transition keeps its existing ordering rather than being pushed a frame later. It also runs
/// inline when there is no UI thread at all — the story tests drive these same view models with no
/// window and no dispatcher, and a marshalling helper that only works inside a running app would
/// make the tests prove less than the app needs.
/// </para>
/// </remarks>
internal static class UiThread
{
    /// <summary>Runs an action on the UI thread, inline if the caller is already on it.</summary>
    internal static void Run(Action work)
    {
        IDispatcher? dispatcher = DispatcherIfMarshallingNeeded();
        if (dispatcher is null)
        {
            work();
            return;
        }

        dispatcher.Dispatch(work);
    }

    /// <summary>Awaits work on the UI thread, inline if the caller is already on it.</summary>
    internal static Task RunAsync(Func<Task> work)
    {
        IDispatcher? dispatcher = DispatcherIfMarshallingNeeded();
        return dispatcher is null ? work() : dispatcher.DispatchAsync(work);
    }

    /// <summary>Awaits work with a result on the UI thread, inline if the caller is already on it.</summary>
    internal static Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        IDispatcher? dispatcher = DispatcherIfMarshallingNeeded();
        return dispatcher is null ? work() : dispatcher.DispatchAsync(work);
    }

    /// <summary>
    /// The application's dispatcher, or null when the caller is already on the UI thread or there is
    /// no UI thread to reach.
    /// </summary>
    /// <remarks>
    /// The dispatcher is asked for rather than <c>MainThread</c>, which probes the platform directly
    /// and throws where no windowing system has been started — including in the story tests, where
    /// that throw arrives on a thread pool thread and aborts the whole test host.
    /// </remarks>
    private static IDispatcher? DispatcherIfMarshallingNeeded()
    {
        IDispatcher? dispatcher = Microsoft.Maui.Controls.Application.Current?.Dispatcher;
        return dispatcher is not null && dispatcher.IsDispatchRequired ? dispatcher : null;
    }
}
