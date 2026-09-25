using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Xml.Linq;

namespace InkWell.Maui.UiTests.Accessibility;

/// <summary>
/// The shared accessibility test apparatus: a keyboard-only journey driver and WCAG 2.1 contrast
/// assertions over the palette the app actually ships.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately splits WCAG 2.1 AA into the half a machine can decide and the half it cannot.
/// Contrast ratios are arithmetic over declared colours, so they are computed here from
/// <c>Resources/Styles/Colors.xaml</c> rather than eyeballed — a token edited to a prettier shade
/// that drops below 4.5:1 fails the build. Keyboard operability is decided by whether a journey can
/// be completed through commands alone, since a step reachable only by a pointer gesture has no
/// command to invoke.
/// </para>
/// <para>
/// What remains for a device is what a device alone can answer: how VoiceOver and TalkBack actually
/// read the editor's <c>contenteditable</c> surface, and how the palette renders on real panels.
/// Those keep T071, T082, T096, T106, and T130 open, and nothing here pretends to close them.
/// </para>
/// </remarks>
public static partial class AccessibilityHarness
{
    /// <summary>WCAG 2.1 AA minimum contrast for body text (SC 1.4.3).</summary>
    public const double AaBodyText = 4.5;

    /// <summary>WCAG 2.1 AA minimum contrast for large text and UI component boundaries (SC 1.4.11).</summary>
    public const double AaLargeTextAndUi = 3.0;

    /// <summary>
    /// Runs a journey as a keyboard user would: every step is a command, invoked in order, and each
    /// must report itself executable before it runs.
    /// </summary>
    /// <param name="steps">The journey, as named commands with their parameters.</param>
    /// <returns>The names of the steps that ran, in order.</returns>
    /// <exception cref="InvalidOperationException">
    /// A step was not executable, which for a keyboard user means a dead end: a control they can
    /// focus but not activate.
    /// </exception>
    public static async Task<IReadOnlyList<string>> WalkAsync(params KeyboardStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var completed = new List<string>(steps.Length);
        foreach (KeyboardStep step in steps)
        {
            step.Before?.Invoke();

            if (!step.Command.CanExecute(step.Parameter))
            {
                throw new InvalidOperationException(
                    $"Keyboard journey stalled at '{step.Name}': the command reports it cannot execute, " +
                    "so a keyboard user could reach this control but not use it.");
            }

            await ExecuteAsync(step.Command, step.Parameter).ConfigureAwait(false);
            completed.Add(step.Name);
        }

        return completed;
    }

    /// <summary>
    /// The WCAG 2.1 contrast ratio between two colours, from 1.0 (identical) to 21.0 (black on
    /// white).
    /// </summary>
    public static double ContrastRatio(Rgb first, Rgb second)
    {
        double a = RelativeLuminance(first);
        double b = RelativeLuminance(second);
        (double lighter, double darker) = a >= b ? (a, b) : (b, a);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Loads the app's colour tokens from <c>Resources/Styles/Colors.xaml</c>, keyed by their
    /// <c>x:Key</c>.
    /// </summary>
    /// <remarks>
    /// Read from the shipping XAML rather than duplicated into the test, so the assertion is about
    /// the palette the app renders and not about a copy that can quietly diverge from it.
    /// </remarks>
    public static IReadOnlyDictionary<string, Rgb> LoadPalette()
    {
        XDocument document = XDocument.Load(PathToMauiFile("Resources", "Styles", "Colors.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2009/xaml";

        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "Color" && element.Attribute(x + "Key") is not null)
            .ToDictionary(
                element => (string)element.Attribute(x + "Key")!,
                element => Rgb.Parse(element.Value.Trim()),
                StringComparer.Ordinal);
    }

    /// <summary>Every colour literal declared in the editor's stylesheet, in source order.</summary>
    /// <remarks>
    /// The CodeMirror surface is styled in CSS rather than XAML, so it would otherwise escape the
    /// palette check entirely — and it is the surface the writer looks at longest.
    /// </remarks>
    public static IReadOnlyList<(string Declaration, Rgb Color)> LoadEditorStyleColors()
    {
        string css = File.ReadAllText(PathToMauiFile("Resources", "Raw", "wwwroot", "styles.css"));

        return
        [
            .. HexColorPattern()
                .Matches(css)
                .Select(match => (
                    Declaration: LineContaining(css, match.Index).Trim(),
                    Color: Rgb.Parse(match.Value)))
        ];
    }

    /// <summary>Resolves a path inside the MAUI app project from the test assembly's location.</summary>
    public static string PathToMauiFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InkWell.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException("Could not locate the repository root from the test assembly.")
            : Path.Combine([directory.FullName, "src", "InkWell.Maui", .. segments]);
    }

    private static Task ExecuteAsync(ICommand command, object? parameter)
    {
        // CommunityToolkit's async commands expose the running Task; a plain ICommand does not, and
        // for those Execute is the whole of it.
        if (command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCommand)
        {
            return asyncCommand.ExecuteAsync(parameter);
        }

        command.Execute(parameter);
        return Task.CompletedTask;
    }

    private static double RelativeLuminance(Rgb color)
    {
        // WCAG 2.1 relative luminance, https://www.w3.org/TR/WCAG21/#dfn-relative-luminance
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static string LineContaining(string text, int index)
    {
        int start = text.LastIndexOf('\n', Math.Min(index, text.Length - 1)) + 1;
        int end = text.IndexOf('\n', index);
        return end < 0 ? text[start..] : text[start..end];
    }

    [GeneratedRegex(@"#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})\b", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorPattern();
}

/// <summary>One step of a keyboard-only journey.</summary>
/// <param name="Name">What the step is, for the failure message when it stalls.</param>
/// <param name="Command">The command a focused control would invoke.</param>
/// <param name="Parameter">Its parameter, if any.</param>
/// <param name="Before">Field entry that precedes the step — typing into a focused entry.</param>
public sealed record KeyboardStep(string Name, ICommand Command, object? Parameter = null, Action? Before = null);

/// <summary>An 8-bit-per-channel colour, parsed from a XAML or CSS hex literal.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>Parses <c>#RGB</c>, <c>#RRGGBB</c>, or <c>#AARRGGBB</c>.</summary>
    public static Rgb Parse(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        string digits = hex.TrimStart('#');

        if (digits.Length == 3)
        {
            digits = string.Concat(digits.Select(c => new string(c, 2)));
        }
        else if (digits.Length == 8)
        {
            // Alpha is dropped: contrast is assessed against the composited result, and every
            // token in this palette is opaque.
            digits = digits[2..];
        }

        return digits.Length != 6
            ? throw new FormatException($"'{hex}' is not a colour literal this harness understands.")
            : new Rgb(
                byte.Parse(digits[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(digits[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
