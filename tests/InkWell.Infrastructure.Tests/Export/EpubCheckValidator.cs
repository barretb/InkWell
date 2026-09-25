using System.Diagnostics;
using System.Text;

namespace InkWell.Infrastructure.Tests.Export;

/// <summary>
/// Runs the W3C's EPUBCheck over an exported book (T120).
/// </summary>
/// <remarks>
/// <para>
/// EPUBCheck is the reference validator, and it catches the class of packaging error that the
/// hand-built exporter is most exposed to — a manifest that disagrees with the archive, an
/// unreferenced resource, XHTML that parses here but not against the EPUB profile. The structural
/// tests in <see cref="EpubExporterTests"/> assert what this project knows to look for; EPUBCheck
/// asserts what the specification says, which is a different and larger thing.
/// </para>
/// <para>
/// It is a Java tool, so it is not vendored into the repository. The validator finds it through the
/// <c>EPUBCHECK_JAR</c> environment variable or at <c>tools/epubcheck/epubcheck.jar</c>, and when it
/// is not installed the test says so out loud rather than passing quietly — a validation step that
/// silently does nothing is worse than not having one.
/// </para>
/// </remarks>
public static class EpubCheckValidator
{
    /// <summary>
    /// Set <c>INKWELL_REQUIRE_EPUBCHECK=1</c> to turn a missing EPUBCheck into a failure. CI sets
    /// it; a developer machine without Java does not have to.
    /// </summary>
    public const string RequireVariable = "INKWELL_REQUIRE_EPUBCHECK";

    /// <summary>The environment variable naming the EPUBCheck jar.</summary>
    public const string JarVariable = "EPUBCHECK_JAR";

    /// <summary>Whether EPUBCheck can be run on this machine.</summary>
    public static bool IsAvailable => FindJar() is not null && FindJava() is not null;

    /// <summary>Whether a missing EPUBCheck should fail the build.</summary>
    public static bool IsRequired =>
        Environment.GetEnvironmentVariable(RequireVariable) is "1" or "true" or "TRUE";

    /// <summary>Why EPUBCheck could not run, for a test to report.</summary>
    public static string UnavailableReason
    {
        get
        {
            if (FindJava() is null)
            {
                return "Java was not found on PATH, so EPUBCheck could not run.";
            }

            return $"EPUBCheck was not found. Set {JarVariable} to the jar, or place it at " +
                "tools/epubcheck/epubcheck.jar. Download it from https://github.com/w3c/epubcheck/releases.";
        }
    }

    /// <summary>
    /// Validates an EPUB file.
    /// </summary>
    /// <param name="epubPath">The file to check.</param>
    /// <returns>The validator's verdict and its full output.</returns>
    public static async Task<EpubCheckResult> ValidateAsync(string epubPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(epubPath);

        string jar = FindJar() ?? throw new InvalidOperationException(UnavailableReason);
        string java = FindJava() ?? throw new InvalidOperationException(UnavailableReason);

        var startInfo = new ProcessStartInfo(java)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(jar);
        startInfo.ArgumentList.Add(epubPath);
        // Warnings fail validation too; honor the process verdict, not just text in its output.
        startInfo.ArgumentList.Add("--failonwarnings");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("EPUBCheck could not be started.");

        var output = new StringBuilder();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().ConfigureAwait(false);
        output.Append(await stdout.ConfigureAwait(false));
        output.Append(await stderr.ConfigureAwait(false));

        return ResultFromExit(process.ExitCode, output.ToString());
    }

    internal static EpubCheckResult ResultFromExit(int exitCode, string report)
    {
        bool hasErrors = report.Contains("ERROR", StringComparison.Ordinal)
            || report.Contains("FATAL", StringComparison.Ordinal);

        return new EpubCheckResult(exitCode == 0 && !hasErrors, $"Exit code: {exitCode}\n{report}");
    }

    private static string? FindJar()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(JarVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InkWell.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return null;
        }

        string vendored = Path.Combine(directory.FullName, "tools", "epubcheck", "epubcheck.jar");
        return File.Exists(vendored) ? vendored : null;
    }

    private static string? FindJava()
    {
        string executable = OperatingSystem.IsWindows() ? "java.exe" : "java";

        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            string candidate = Path.Combine(javaHome, "bin", executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder.Trim(), executable))
            .FirstOrDefault(File.Exists);
    }
}

/// <summary>The outcome of an EPUBCheck run.</summary>
/// <param name="IsValid">Whether the book validated with no errors.</param>
/// <param name="Report">EPUBCheck's full output, so a failure can say what was wrong.</param>
public sealed record EpubCheckResult(bool IsValid, string Report);
