using InkWell.Domain.Services;

namespace InkWell.Domain.Tests.Services;

/// <summary>
/// FR-009 / SC-005: the count the writer sees must match an independent count of reader-facing
/// prose. Markdown syntax tokens, link targets, and inline-image markers are never words.
/// </summary>
public class ProseWordCounterTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   \r\n\t  ", 0)]
    public void Empty_content_counts_zero(string? markdown, int expected)
        => Assert.Equal(expected, ProseWordCounter.Count(markdown));

    [Fact]
    public void Plain_prose_counts_whitespace_delimited_words()
    {
        Assert.Equal(10, ProseWordCounter.Count("The snow came early that year, and it never left."));
    }

    [Fact]
    public void Irregular_whitespace_and_newlines_do_not_inflate_the_count()
    {
        Assert.Equal(6, ProseWordCounter.Count("She   waited.\n\nThe  door\tstayed shut."));
    }

    [Fact]
    public void Heading_markers_are_not_counted_but_heading_text_is()
    {
        // "Chapter One" is prose the reader sees; the leading "##" is syntax.
        Assert.Equal(2, ProseWordCounter.Count("## Chapter One"));
    }

    [Theory]
    [InlineData("**bold**", 1)]
    [InlineData("*italic*", 1)]
    [InlineData("***both***", 1)]
    [InlineData("~~struck~~", 1)]
    [InlineData("He was **never** coming back.", 5)]
    public void Emphasis_syntax_is_excluded_from_the_count(string markdown, int expected)
        => Assert.Equal(expected, ProseWordCounter.Count(markdown));

    [Fact]
    public void Emphasis_inside_a_word_still_counts_as_one_word()
    {
        // "un**bloody**likely" renders as a single word to the reader.
        Assert.Equal(1, ProseWordCounter.Count("un**bloody**likely"));
    }

    [Fact]
    public void List_markers_are_not_counted_but_item_text_is()
    {
        const string markdown = """
            - the first thing
            - the second thing
            """;

        Assert.Equal(6, ProseWordCounter.Count(markdown));
    }

    [Fact]
    public void Ordered_list_numbers_are_not_counted()
    {
        const string markdown = """
            1. first
            2. second
            """;

        Assert.Equal(2, ProseWordCounter.Count(markdown));
    }

    [Fact]
    public void Blockquote_markers_are_not_counted()
        => Assert.Equal(3, ProseWordCounter.Count("> she said nothing"));

    [Fact]
    public void Link_text_is_counted_but_the_target_is_not()
    {
        // The reader sees "the old mill"; "https://example.com/mill" is markup.
        Assert.Equal(3, ProseWordCounter.Count("[the old mill](https://example.com/mill)"));
    }

    [Fact]
    public void Link_inside_a_sentence_counts_only_the_visible_words()
    {
        Assert.Equal(6, ProseWordCounter.Count("She walked to [the old mill](https://example.com/mill)."));
    }

    [Fact]
    public void Autolinks_are_not_counted_as_prose()
        => Assert.Equal(0, ProseWordCounter.Count("<https://example.com/a/b/c>"));

    [Fact]
    public void Inline_image_markers_are_never_counted()
    {
        // Neither the marker nor the alt text is prose the reader reads (FR-009).
        Assert.Equal(0, ProseWordCounter.Count("![a photograph of the mill](inkwell-img://abc)"));
    }

    [Fact]
    public void Data_uri_images_are_never_counted()
        => Assert.Equal(0, ProseWordCounter.Count("![alt text here](data:image/png;base64,iVBORw0KGgoAAAANSUhEUg==)"));

    [Fact]
    public void Prose_around_an_image_is_still_counted()
    {
        const string markdown = "Before the storm ![the mill](inkwell-img://abc) after the storm";

        Assert.Equal(6, ProseWordCounter.Count(markdown));
    }

    [Fact]
    public void Fenced_code_blocks_are_not_prose()
    {
        const string markdown = """
            Real prose here.

            ```
            var x = compute(1, 2, 3);
            ```
            """;

        Assert.Equal(3, ProseWordCounter.Count(markdown));
    }

    [Fact]
    public void Inline_html_tags_are_not_counted_but_their_text_is()
        => Assert.Equal(5, ProseWordCounter.Count("She said <em>nothing</em> at all."));

    [Fact]
    public void An_html_block_contributes_no_prose()
        => Assert.Equal(0, ProseWordCounter.Count("<div class=\"note\">\n  markup only\n</div>"));

    [Fact]
    public void Horizontal_rules_and_blank_lines_count_nothing()
        => Assert.Equal(0, ProseWordCounter.Count("---\n\n***\n\n"));

    [Fact]
    public void A_realistic_chapter_matches_an_independent_prose_count()
    {
        const string markdown = """
            # The Long Winter

            The snow came early that year. It fell for **nine** days without pause, and when it
            stopped the village had gone quiet in a way that Elin had never heard before.

            ![the frozen mill](inkwell-img://8f1c)

            > Nothing moves out there, her mother said.

            - bread
            - lamp oil
            - rope

            She read the notice at [the old mill](https://example.com/notice) twice.
            """;

        // Independently counted reader-facing words:
        //   heading           "The Long Winter"                                     ->  3
        //   paragraph 1       "The snow came ... never heard before." ("nine" once) -> 31
        //   image             marker and alt text both excluded                     ->  0
        //   blockquote        "Nothing moves out there, her mother said."           ->  7
        //   list items        bread / lamp oil / rope                               ->  4
        //   final paragraph   "She read the notice at the old mill twice."          ->  9
        Assert.Equal(54, ProseWordCounter.Count(markdown));
    }

    [Fact]
    public void Counting_is_deterministic()
    {
        const string markdown = "The snow came early that year.";

        Assert.Equal(ProseWordCounter.Count(markdown), ProseWordCounter.Count(markdown));
    }
}
