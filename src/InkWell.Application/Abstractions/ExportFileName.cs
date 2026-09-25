using System.Text.RegularExpressions;
using InkWell.Application.Abstractions.Dtos;

namespace InkWell.Application.Abstractions;

/// <summary>
/// Turns a manuscript or chapter title into a file name every platform will accept.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the application layer because both sides of an export need it and neither may reference
/// the other: the presentation layer uses it to suggest a name in the save dialog, and the exporter
/// uses it to name the files in a per-chapter batch. Two copies would be two chances for the name
/// InkWell suggests to differ from the name it writes.
/// </para>
/// <para>
/// It only ever produces a *suggestion* or a batch file name. Where the writer confirms a path in a
/// save dialog, that path is used exactly as they gave it.
/// </para>
/// </remarks>
public static partial class ExportFileName
{
    /// <summary>
    /// Comfortably inside every platform's limit once a long folder path is prefixed, and short
    /// enough to stay readable in a file manager.
    /// </summary>
    public const int MaxLength = 80;

    /// <summary>The conventional file extension for a format.</summary>
    public static string ExtensionFor(ExportFormat format) => format switch
    {
        ExportFormat.Epub => ".epub",
        ExportFormat.Pdf => ".pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Builds a file name for <paramref name="title"/> in <paramref name="format"/>.</summary>
    public static string For(string? title, ExportFormat format) => Sanitize(title) + ExtensionFor(format);

    /// <summary>
    /// Strips the characters Windows, macOS, and Android between them refuse, and tidies the result.
    /// </summary>
    /// <returns>A usable name; "Untitled" when nothing usable remains.</returns>
    public static string Sanitize(string? title)
    {
        // Removing a character leaves a gap beside the space already there, so "Chapter 1: The Mill"
        // would otherwise come out as "Chapter 1  The Mill".
        string cleaned = CollapseWhitespace()
            .Replace(UnsafeCharacters().Replace(title ?? string.Empty, " "), " ")
            .Trim();

        if (cleaned.Length == 0)
        {
            cleaned = "Untitled";
        }

        return cleaned.Length > MaxLength ? cleaned[..MaxLength].TrimEnd() : cleaned;
    }

    [GeneratedRegex("[\\\\/:*?\"<>|\\x00-\\x1F]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeCharacters();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex CollapseWhitespace();
}
