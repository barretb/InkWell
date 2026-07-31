using System.Text.RegularExpressions;
using InkWell.Application.Abstractions;
using Markdig;
using Markdig.Syntax;

namespace InkWell.Infrastructure.Markdown;

/// <summary>
/// Markdig-backed rendering and parsing of chapter markdown.
/// </summary>
/// <remarks>
/// The XHTML path exists because Markdig emits HTML5 — <c>&lt;img src="…"&gt;</c>, <c>&lt;br&gt;</c>,
/// <c>&lt;hr&gt;</c> — and an EPUB's content documents must be well-formed XML or EPUBCheck rejects
/// the book and some readers refuse to open it (research.md §3). Rather than post-process with an
/// XML parser, which would also have to cope with the raw HTML a writer may have typed, the void
/// elements are closed with a targeted rewrite and the document is wrapped in a namespaced root.
/// </remarks>
public sealed partial class MarkdownService : IMarkdownService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .Build();

    /// <inheritdoc />
    public string ToHtml(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Markdig.Markdown.ToHtml(markdown, Pipeline);
    }

    /// <inheritdoc />
    public string ToXhtml(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return CloseVoidElements(ToHtml(markdown));
    }

    /// <inheritdoc />
    public MarkdownDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Markdig.Markdown.Parse(markdown, Pipeline);
    }

    /// <summary>
    /// Rewrites HTML5 void elements into self-closing XHTML form, leaving already self-closed tags
    /// untouched.
    /// </summary>
    /// <param name="html">Rendered HTML5.</param>
    /// <returns>Well-formed XHTML fragment markup.</returns>
    public static string CloseVoidElements(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return VoidElementPattern().Replace(html, match =>
        {
            string inner = match.Groups["inner"].Value.TrimEnd();
            return inner.EndsWith('/') ? $"<{inner}>" : $"<{inner} />";
        });
    }

    // Matches an opening tag for an HTML void element, capturing the tag name and its attributes.
    [GeneratedRegex(
        @"<(?<inner>(?:img|br|hr|meta|link|area|base|col|embed|input|param|source|track|wbr)\b[^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VoidElementPattern();
}
