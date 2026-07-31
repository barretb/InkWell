using InkWell.Infrastructure.Persistence;

namespace InkWell.Maui;

/// <summary>The application object.</summary>
/// <remarks>
/// The base type is fully qualified throughout this project: the unqualified name
/// <c>Application</c> also matches the <c>InkWell.Application</c> layer namespace, and letting the
/// compiler guess between a MAUI type and an architectural layer is a trap worth closing.
/// </remarks>
public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly ISqliteConnectionFactory _database;

    /// <summary>Creates the application.</summary>
    /// <param name="database">The encrypted store, so it can be checkpointed on suspend.</param>
    public App(ISqliteConnectionFactory database)
    {
        InitializeComponent();
        _database = database;
    }

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());

        // Folding the write-ahead log into the database on suspend is what makes the durability
        // promise hold across an OS-initiated kill: a committed but un-checkpointed WAL can
        // otherwise look like lost work on next launch (research.md §2, SC-003).
        window.Stopped += async (_, _) => await _database.CheckpointAsync();
        window.Destroying += async (_, _) => await _database.CheckpointAsync();

        return window;
    }
}
