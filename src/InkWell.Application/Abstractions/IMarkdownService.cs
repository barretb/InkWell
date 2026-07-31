using Markdig.Syntax;

namespace InkWell.Application.Abstractions;

/// <summary>
/// Renders and parses chapter markdown for export (contracts/export-service.md).
/// </summary>
public interface IMarkdownService
{
    /// <summary>Renders markdown to HTML5.</summary>
    string ToHtml(string markdown);

    /// <summary>
    /// Renders markdown to well-formed XHTML: void elements are closed and the XHTML namespace is
    /// present. EPUB readers and EPUBCheck reject the HTML5 that Markdig emits by default
    /// (research.md §3).
    /// </summary>
    string ToXhtml(string markdown);

    /// <summary>
    /// Parses markdown to its syntax tree, for walkers that map nodes onto MigraDoc elements or
    /// rewrite image references.
    /// </summary>
    MarkdownDocument Parse(string markdown);
}
