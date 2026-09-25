using System.Diagnostics;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Maui.UiTests.Harness;
using InkWell.Presentation.ViewModels;
using Xunit.Abstractions;

namespace InkWell.Maui.UiTests.Performance;

/// <summary>
/// T122 · SC-004 — a 150,000-word manuscript across 52 chapters stays responsive to open, list,
/// and type in.
/// </summary>
/// <remarks>
/// <para>
/// The budgets below are for the work InkWell actually controls: a store query, a word-count
/// recompute, an autosave transaction. Rendering a frame is MAUI's business and is not measured
/// here — what is measured is whether the app hands the UI thread anything large enough to make
/// missing a frame inevitable.
/// </para>
/// <para>
/// They are set well above the observed times rather than at them. A performance test tuned to the
/// current machine fails on a slower build agent for no reason anyone can act on; one set at the
/// point where a writer would actually notice fails only when something has genuinely regressed.
/// </para>
/// </remarks>
[Collection("Performance")]
public class LargeManuscriptPerformanceTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// The keystroke budget. SC-004 asks for feedback perceived as instantaneous; the constitution
    /// puts a frame at 16 ms. What is timed is the per-keystroke work the app does before the
    /// editor can echo — not the echo itself, which happens inside CodeMirror.
    /// </summary>
    private const int KeystrokeBudgetMs = 16;

    /// <summary>What a writer would experience as "the chapter opened at once".</summary>
    private const int ChapterOpenBudgetMs = 400;

    /// <summary>What a writer would experience as "the chapter list appeared at once".</summary>
    private const int ListBudgetMs = 400;

    [Fact]
    public async Task The_seeded_manuscript_really_is_the_scale_the_criterion_names()
    {
        // A performance number is only meaningful if the manuscript behind it is the right size.
        await using var app = new AppHarness();
        SeededManuscript seeded = await LargeManuscriptSeeder.SeedAsync(app);

        int words = await app.ChapterUseCases.GetManuscriptWordCountAsync(seeded.ManuscriptId);
        IReadOnlyList<ChapterSummary> chapters = await app.ChapterUseCases.ListAsync(seeded.ManuscriptId);

        _output.WriteLine($"Seeded {chapters.Count} chapters, {words:N0} prose words.");

        Assert.True(words >= 150_000, $"Seeded only {words:N0} words; SC-004 is about 150,000.");
        Assert.True(chapters.Count >= 50, $"Seeded only {chapters.Count} chapters; SC-004 is about 50+.");
    }

    [Fact]
    public async Task Listing_fifty_chapters_never_loads_their_prose()
    {
        // The design decision SC-004 rests on: the chapter list is titles and counts, so opening a
        // 150,000-word manuscript reads kilobytes rather than megabytes.
        await using var app = new AppHarness();
        SeededManuscript seeded = await LargeManuscriptSeeder.SeedAsync(app);

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<ChapterSummary> chapters = await app.ChapterUseCases.ListAsync(seeded.ManuscriptId);
        stopwatch.Stop();

        _output.WriteLine($"Listed {chapters.Count} chapters in {stopwatch.ElapsedMilliseconds} ms.");

        Assert.True(
            stopwatch.ElapsedMilliseconds < ListBudgetMs,
            $"Listing {chapters.Count} chapters took {stopwatch.ElapsedMilliseconds} ms, over the {ListBudgetMs} ms budget.");
    }

    [Fact]
    public async Task Opening_the_manuscript_screen_stays_responsive()
    {
        await using var app = new AppHarness();
        SeededManuscript seeded = await LargeManuscriptSeeder.SeedAsync(app);

        ManuscriptViewModel manuscript = app.Manuscript;
        manuscript.ManuscriptId = seeded.ManuscriptId;

        var stopwatch = Stopwatch.StartNew();
        await manuscript.LoadAsync();
        stopwatch.Stop();

        _output.WriteLine($"Opened the manuscript in {stopwatch.ElapsedMilliseconds} ms.");

        Assert.Equal(LargeManuscriptSeeder.TargetChapterCount, manuscript.Chapters.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < ListBudgetMs,
            $"Opening the manuscript took {stopwatch.ElapsedMilliseconds} ms, over the {ListBudgetMs} ms budget.");
    }

    [Fact]
    public async Task Opening_a_chapter_loads_that_chapter_and_not_the_book()
    {
        // One CodeMirror instance per chapter is the whole scaling argument (research.md §1). A
        // chapter deep in the manuscript must cost the same as the first one.
        await using var app = new AppHarness();
        SeededManuscript seeded = await LargeManuscriptSeeder.SeedAsync(app);

        var first = Stopwatch.StartNew();
        DomainResultTiming firstOpen = await OpenAsync(app, seeded.ChapterIds[0]);
        first.Stop();

        var last = Stopwatch.StartNew();
        DomainResultTiming lastOpen = await OpenAsync(app, seeded.ChapterIds[^1]);
        last.Stop();

        _output.WriteLine($"First chapter: {firstOpen.ElapsedMs} ms, {firstOpen.Characters:N0} characters.");
        _output.WriteLine($"Last chapter:  {lastOpen.ElapsedMs} ms, {lastOpen.Characters:N0} characters.");

        Assert.True(
            lastOpen.ElapsedMs < ChapterOpenBudgetMs,
            $"Opening the last chapter took {lastOpen.ElapsedMs} ms, over the {ChapterOpenBudgetMs} ms budget.");

        // A chapter is a few thousand words, never the whole 150,000.
        Assert.True(
            lastOpen.Characters < 100_000,
            $"Opening one chapter pulled {lastOpen.Characters:N0} characters; it should be one chapter's worth.");
    }

    [Fact]
    public async Task Typing_in_a_full_manuscript_costs_no_more_than_a_frame()
    {
        // The per-keystroke work: recompute this chapter's prose count and hand the editor its
        // updated numbers. Everything heavier — the transaction, the manuscript total — happens on
        // the debounced commit, off this path (research.md §2).
        await using var app = new AppHarness(autoSaveDebounce: TimeSpan.FromMinutes(10));
        SeededManuscript seeded = await LargeManuscriptSeeder.SeedAsync(app);

        app.Editor.ManuscriptId = seeded.ManuscriptId;
        app.Editor.ChapterId = seeded.ChapterIds[^1];
        await app.Editor.LoadAsync();

        // Warm up: the first keystroke pays for JIT, not for the manuscript's size.
        app.EditorHost.Type(seeded.ChapterIds[^1], "x");

        const int keystrokes = 200;
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < keystrokes; i++)
        {
            app.EditorHost.Type(seeded.ChapterIds[^1], "a");
        }

        stopwatch.Stop();
        double perKeystroke = stopwatch.Elapsed.TotalMilliseconds / keystrokes;

        _output.WriteLine($"{keystrokes} keystrokes in {stopwatch.ElapsedMilliseconds} ms ({perKeystroke:F3} ms each).");

        Assert.True(
            perKeystroke < KeystrokeBudgetMs,
            $"Each keystroke cost {perKeystroke:F3} ms against a {KeystrokeBudgetMs} ms frame budget.");
    }

    [Fact]
    public async Task An_autosave_commit_in_a_full_manuscript_stays_well_inside_the_debounce()
    {
        // A commit that took longer than the pause that triggers it would queue behind itself and
        // turn a pause in typing into a stutter.
        await using var app = new AppHarness(autoSaveDebounce: TimeSpan.FromMinutes(10));
        SeededManuscript seeded = await LargeManuscriptSeeder.SeedAsync(app);

        Guid chapterId = seeded.ChapterIds[^1];
        app.Editor.ManuscriptId = seeded.ManuscriptId;
        app.Editor.ChapterId = chapterId;
        await app.Editor.LoadAsync();

        app.EditorHost.Type(chapterId, " One more sentence for the ridge.");
        await app.Editor.FlushAsync();

        app.EditorHost.Type(chapterId, " And another one after it.");

        var stopwatch = Stopwatch.StartNew();
        await app.Editor.FlushAsync();
        stopwatch.Stop();

        _output.WriteLine($"Autosave commit took {stopwatch.ElapsedMilliseconds} ms.");

        // The debounce floor is ~500 ms (research.md §2); a commit must finish comfortably inside it.
        Assert.True(
            stopwatch.ElapsedMilliseconds < 500,
            $"An autosave commit took {stopwatch.ElapsedMilliseconds} ms, which is longer than the debounce that schedules it.");
    }

    [Fact]
    public async Task The_manuscript_word_count_is_a_sum_and_not_a_recount()
    {
        // FR-009's manuscript total is the sum of cached chapter counts. If it ever became a
        // re-parse of every chapter, this is where it would show.
        await using var app = new AppHarness();
        SeededManuscript seeded = await LargeManuscriptSeeder.SeedAsync(app);

        var stopwatch = Stopwatch.StartNew();
        int words = await app.ChapterUseCases.GetManuscriptWordCountAsync(seeded.ManuscriptId);
        stopwatch.Stop();

        _output.WriteLine($"Counted {words:N0} words in {stopwatch.ElapsedMilliseconds} ms.");

        Assert.True(
            stopwatch.ElapsedMilliseconds < 100,
            $"Totalling the manuscript took {stopwatch.ElapsedMilliseconds} ms; it should be one aggregate query.");
    }

    private static async Task<DomainResultTiming> OpenAsync(AppHarness app, Guid chapterId)
    {
        var stopwatch = Stopwatch.StartNew();
        ChapterContent content = (await app.ChapterUseCases.GetContentAsync(chapterId)).Value;
        stopwatch.Stop();

        return new DomainResultTiming(stopwatch.ElapsedMilliseconds, content.ContentMarkdown.Length);
    }

    private sealed record DomainResultTiming(long ElapsedMs, int Characters);
}

/// <summary>
/// Keeps the performance tests off the same cores as the rest of the suite.
/// </summary>
/// <remarks>
/// xUnit runs collections in parallel by default, and a timing assertion competing with a dozen
/// SQLCipher fixtures measures the contention rather than the app.
/// </remarks>
[CollectionDefinition("Performance", DisableParallelization = true)]
public sealed class PerformanceTestGroup;
