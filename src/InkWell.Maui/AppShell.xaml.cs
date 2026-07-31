using InkWell.Maui.Views;
using InkWell.Presentation;

namespace InkWell.Maui;

/// <summary>
/// The app's navigation host. The library is the root; the manuscript and editor screens are pushed
/// onto it, so Back always leads home.
/// </summary>
public partial class AppShell : Shell
{
    /// <summary>Creates the shell and registers its pushed routes.</summary>
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(Routes.Manuscript, typeof(ManuscriptPage));
        Routing.RegisterRoute(Routes.Editor, typeof(EditorPage));
        Routing.RegisterRoute(Routes.Goals, typeof(GoalsPage));
        Routing.RegisterRoute(Routes.Characters, typeof(CharactersPage));
        Routing.RegisterRoute(Routes.PlotThreads, typeof(PlotThreadsPage));
    }
}
