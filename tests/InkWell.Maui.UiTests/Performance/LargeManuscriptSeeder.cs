using System.Text;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Maui.UiTests.Harness;

namespace InkWell.Maui.UiTests.Performance;

/// <summary>
/// Builds the manuscript SC-004 is written about: 150,000+ words across 50+ chapters, in the real
/// encrypted store.
/// </summary>
/// <remarks>
/// <para>
/// Seeded through the same transactional autosave path the editor uses, not by bulk-inserting rows.
/// A performance claim measured against a database populated by a shortcut is a claim about the
/// shortcut: the word counts, the manuscript timestamps, and the daily writing records all have to
/// be there, because they are what the real queries read.
/// </para>
/// <para>
/// The prose is generated from a small vocabulary rather than repeated filler, so chapters differ in
/// length and content the way a real book's do, and so nothing in the store can accidentally
/// deduplicate its way to a flattering number.
/// </para>
/// </remarks>
public static class LargeManuscriptSeeder
{
    /// <summary>The word count SC-004 names as the scale InkWell must stay responsive at.</summary>
    public const int TargetWordCount = 150_000;

    /// <summary>The chapter count SC-004 names.</summary>
    public const int TargetChapterCount = 52;

    private static readonly string[] Vocabulary =
    [
        "snow", "mill", "ridge", "winter", "ledger", "ash", "river", "lantern", "door", "field",
        "she", "he", "they", "watched", "waited", "walked", "burned", "remembered", "counted", "held",
        "the", "a", "and", "but", "though", "because", "until", "while", "before", "after",
        "cold", "quiet", "grey", "long", "distant", "familiar", "unfinished", "certain",
    ];

    /// <summary>
    /// Fills <paramref name="app"/> with a full-scale manuscript.
    /// </summary>
    /// <param name="app">The harness to seed.</param>
    /// <param name="chapterCount">How many chapters to write.</param>
    /// <param name="totalWords">How many prose words to spread across them.</param>
    /// <returns>The manuscript and its chapters, in order.</returns>
    public static async Task<SeededManuscript> SeedAsync(
        AppHarness app,
        int chapterCount = TargetChapterCount,
        int totalWords = TargetWordCount)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentOutOfRangeException.ThrowIfLessThan(chapterCount, 1);

        Manuscript manuscript = (await app.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;

        // A fixed seed: a performance test that varies run to run cannot be compared with itself.
        var random = new Random(20260902);
        int perChapter = totalWords / chapterCount;
        var chapterIds = new List<Guid>(chapterCount);
        int written = 0;

        for (int i = 0; i < chapterCount; i++)
        {
            Chapter chapter = (await app.ChapterUseCases
                .AddAsync(manuscript.Id, $"Chapter {i + 1}")
                .ConfigureAwait(false)).Value;

            // The last chapter takes the remainder, so the total is exact rather than approximate.
            int words = i == chapterCount - 1 ? totalWords - written : perChapter;
            string markdown = GenerateChapter(random, words, i);

            await app.ChapterRepository.CommitAutoSaveAsync(new AutoSaveCommit(
                chapter.Id,
                markdown,
                Domain.Services.ProseWordCounter.Count(markdown),
                app.Clock.Now,
                app.Clock.Today)).ConfigureAwait(false);

            chapterIds.Add(chapter.Id);
            written += words;
        }

        return new SeededManuscript(manuscript.Id, chapterIds);
    }

    /// <summary>
    /// Writes one chapter's markdown with roughly <paramref name="wordCount"/> prose words.
    /// </summary>
    /// <remarks>
    /// Headings and emphasis are included on purpose: they are markdown syntax, they are excluded
    /// from the prose count (FR-009), and a word counter that quietly counted them would inflate
    /// every number in this suite.
    /// </remarks>
    private static string GenerateChapter(Random random, int wordCount, int index)
    {
        var builder = new StringBuilder(wordCount * 7);
        builder.Append("# Chapter ").Append(index + 1).Append("\n\n");

        int remaining = wordCount;
        while (remaining > 0)
        {
            int sentence = Math.Min(remaining, random.Next(8, 20));
            for (int i = 0; i < sentence; i++)
            {
                string word = Vocabulary[random.Next(Vocabulary.Length)];

                // Occasional emphasis, so the counter has syntax to exclude.
                if (i > 0 && random.Next(40) == 0)
                {
                    builder.Append("**").Append(word).Append("** ");
                }
                else
                {
                    builder.Append(word).Append(' ');
                }
            }

            builder.Length--;
            builder.Append(". ");
            remaining -= sentence;

            if (random.Next(6) == 0)
            {
                builder.Append("\n\n");
            }
        }

        return builder.ToString().TrimEnd();
    }
}

/// <summary>A seeded manuscript and its chapters, in reading order.</summary>
/// <param name="ManuscriptId">The manuscript.</param>
/// <param name="ChapterIds">Its chapters, in order.</param>
public sealed record SeededManuscript(Guid ManuscriptId, IReadOnlyList<Guid> ChapterIds);
