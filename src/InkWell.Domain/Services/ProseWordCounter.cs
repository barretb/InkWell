using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace InkWell.Domain.Services;

/// <summary>
/// Counts the words a reader would actually read in a chapter's markdown (FR-009, SC-005).
/// </summary>
/// <remarks>
/// <para>
/// The count the writer sees drives their daily goal, so it has to agree with an independent count
/// of the rendered prose. That rules out counting the raw markdown: <c>**never**</c> is one word,
/// not three tokens, and <c>![a photo of the mill](inkwell-img://…)</c> is zero words even though
/// it contains six.
/// </para>
/// <para>
/// So the counter walks the Markdig AST rather than the source text, and applies these rules:
/// </para>
/// <list type="bullet">
///   <item>Literal text and code spans are prose; headings, list items, and quotes are prose too.</item>
///   <item>Image nodes contribute nothing — neither the marker nor the alt text is read as prose.</item>
///   <item>A link contributes its visible label, never its target.</item>
///   <item>Autolinks, raw HTML, and code blocks contribute nothing.</item>
/// </list>
/// <para>
/// Text is accumulated per block before being split, not per inline node, so emphasis inside a
/// word (<c>un**bloody**likely</c>) still counts as the single word the reader sees.
/// </para>
/// </remarks>
public static class ProseWordCounter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    /// <summary>
    /// Counts reader-facing prose words in a markdown document.
    /// </summary>
    /// <param name="markdown">The chapter's markdown. May be null or empty.</param>
    /// <returns>The number of whitespace-delimited prose words; zero for empty content.</returns>
    public static int Count(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return 0;
        }

        MarkdownDocument document = Markdown.Parse(markdown, Pipeline);
        var words = 0;
        var buffer = new StringBuilder();

        CountBlock(document, buffer, ref words);

        return words;
    }

    private static void CountBlock(Block block, StringBuilder buffer, ref int words)
    {
        switch (block)
        {
            // Code blocks and raw HTML blocks are markup, not prose.
            case CodeBlock:
            case HtmlBlock:
                return;

            case ContainerBlock container:
                foreach (Block child in container)
                {
                    CountBlock(child, buffer, ref words);
                }

                return;

            case LeafBlock leaf:
                if (leaf.Inline is null)
                {
                    return;
                }

                buffer.Clear();
                AppendInlines(leaf.Inline, buffer);
                words += CountWords(buffer);
                return;

            default:
                return;
        }
    }

    private static void AppendInlines(ContainerInline container, StringBuilder buffer)
    {
        for (Inline? inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            AppendInline(inline, buffer);
        }
    }

    private static void AppendInline(Inline inline, StringBuilder buffer)
    {
        switch (inline)
        {
            case LinkInline { IsImage: true }:
                // FR-009: neither the image marker nor its alt text is prose.
                return;

            case LinkInline link:
                // The label is what the reader sees; link.Url is markup.
                AppendInlines(link, buffer);
                return;

            case AutolinkInline:
            case HtmlInline:
            case HtmlEntityInline:
                return;

            case LiteralInline literal:
                buffer.Append(literal.Content.AsSpan());
                return;

            case CodeInline code:
                buffer.Append(code.Content);
                return;

            case LineBreakInline:
                buffer.Append(' ');
                return;

            case ContainerInline nested:
                // Emphasis, strong, and the like: markers are dropped, content is kept, and no
                // separator is inserted so mid-word emphasis stays one word.
                AppendInlines(nested, buffer);
                return;

            default:
                return;
        }
    }

    private static int CountWords(StringBuilder buffer)
    {
        var words = 0;
        var inWord = false;

        for (var i = 0; i < buffer.Length; i++)
        {
            if (char.IsWhiteSpace(buffer[i]))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }

        return words;
    }
}
