using System.Text.RegularExpressions;
using InkWell.Maui.UiTests.Accessibility;

namespace InkWell.Maui.UiTests.Performance;

/// <summary>
/// T123 · Constitution §V — no blocking I/O on the UI thread, enforced rather than reviewed.
/// </summary>
/// <remarks>
/// <para>
/// An audit is only worth as much as the day it was done on. These are source-level checks so the
/// result survives: the next `.Result` anyone adds to a view model fails the build, on the day it is
/// added, rather than being found later as a stutter nobody can reproduce.
/// </para>
/// <para>
/// They deliberately look at the layers that run on the UI thread — the view models, the editor host,
/// the pages — and not at infrastructure, where a synchronous call inside a repository is already
/// behind an async boundary.
/// </para>
/// </remarks>
public partial class UiThreadDisciplineTests
{
    private static IEnumerable<(string Path, string Text)> SourceFiles(params string[] projectRelativeRoots)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "InkWell.slnx")))
        {
            repository = repository.Parent;
        }

        Assert.NotNull(repository);

        foreach (string relative in projectRelativeRoots)
        {
            string root = Path.Combine(repository!.FullName, relative);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return (Path.GetRelativePath(repository.FullName, file), File.ReadAllText(file));
            }
        }
    }

    [Fact]
    public void Nothing_on_the_ui_path_blocks_on_a_task()
    {
        // `.Result`, `.Wait()`, and `GetAwaiter().GetResult()` on the UI thread are how a save turns
        // into a frozen window, and on some MAUI targets into a deadlock.
        List<string> offenders =
        [
            .. SourceFiles("src/InkWell.Presentation", "src/InkWell.Maui")
                .Where(file => BlockingWaitPattern().IsMatch(file.Text))
                .Select(file => $"{file.Path}: {BlockingWaitPattern().Match(file.Text).Value.Trim()}")
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Nothing_on_the_ui_path_sleeps()
    {
        List<string> offenders =
        [
            .. SourceFiles("src/InkWell.Presentation", "src/InkWell.Maui")
                .Where(file => file.Text.Contains("Thread.Sleep", StringComparison.Ordinal))
                .Select(file => file.Path)
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_async_void_is_an_event_handler_or_a_lifecycle_override()
    {
        // `async void` is correct exactly where the framework demands it — an event handler, a MAUI
        // page override — and is an unobservable failure anywhere else.
        var allowed = new[]
        {
            "OnAppearing", "OnDisappearing",
        };

        List<string> offenders = [];
        foreach ((string path, string text) in SourceFiles("src/InkWell.Presentation", "src/InkWell.Maui"))
        {
            foreach (Match match in AsyncVoidPattern().Matches(text))
            {
                string name = match.Groups["name"].Value;
                string parameters = match.Groups["parameters"].Value;

                bool isEventHandler = parameters.Contains("object? sender", StringComparison.Ordinal)
                    || parameters.Contains("object sender", StringComparison.Ordinal);

                if (!isEventHandler && !allowed.Contains(name))
                {
                    offenders.Add($"{path}: async void {name}({parameters})");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Export_is_pushed_off_the_ui_thread()
    {
        // Rendering a 150,000-word manuscript with images is the longest single operation the app
        // performs, and it is started from a button. It has to leave the UI thread.
        string exportViewModel = SourceFiles("src/InkWell.Presentation")
            .Single(file => file.Path.EndsWith("ExportViewModel.cs", StringComparison.Ordinal))
            .Text;

        Assert.Contains("Task.Run(", exportViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void The_editor_page_never_reloads_a_chapter_it_already_has_open()
    {
        // Reloading on every appearance would make returning from a character lookup cost a full
        // chapter read — and would lose the writer's place, which FR-015 exists to prevent.
        string editorPage = File.ReadAllText(
            AccessibilityHarness.PathToMauiFile("Views", "EditorPage.xaml.cs"));

        Assert.Contains("_hasLoaded", editorPage, StringComparison.Ordinal);
        Assert.Contains("ResumeWritingAsync", editorPage, StringComparison.Ordinal);
    }

    [GeneratedRegex(
        @"\.Result\b|\.Wait\(\)|GetAwaiter\(\)\s*\.\s*GetResult\(\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex BlockingWaitPattern();

    [GeneratedRegex(
        @"async\s+void\s+(?<name>\w+)\s*\((?<parameters>[^)]*)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex AsyncVoidPattern();
}
