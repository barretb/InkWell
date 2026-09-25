using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;
using InkWell.Maui.UiTests.Harness;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.UiTests.Accessibility;

/// <summary>
/// US1 · FR-019, SC-007 — drafting and organising a manuscript is completable by keyboard alone,
/// including chapter reorder; structure and outcomes are announced in words; and the palette the
/// screens use meets WCAG 2.1 AA contrast.
/// </summary>
public class UserStory1AccessibilityTests
{
    // ---- Keyboard-only completion of the whole journey ----

    [Fact]
    public async Task The_whole_drafting_journey_is_completable_without_a_pointer()
    {
        await using var app = new AppHarness();
        LibraryViewModel library = app.Library;

        IReadOnlyList<string> walked = await AccessibilityHarness.WalkAsync(
            new KeyboardStep("open the library", library.LoadCommand),
            new KeyboardStep(
                "name the manuscript",
                library.CreateCommand,
                Before: () => library.NewManuscriptTitle = "The Long Winter"));

        Assert.Equal(["open the library", "name the manuscript"], walked);
        Assert.Single(library.Manuscripts);

        ManuscriptSummary created = library.Manuscripts[0];
        ManuscriptViewModel manuscript = app.Manuscript;
        manuscript.ManuscriptId = created.Id;

        await AccessibilityHarness.WalkAsync(
            new KeyboardStep("open the manuscript", manuscript.LoadCommand),
            new KeyboardStep("add chapter one", manuscript.AddChapterCommand,
                Before: () => manuscript.NewChapterTitle = "Snowfall"),
            new KeyboardStep("add chapter two", manuscript.AddChapterCommand,
                Before: () => manuscript.NewChapterTitle = "The Mill"),
            new KeyboardStep("add chapter three", manuscript.AddChapterCommand,
                Before: () => manuscript.NewChapterTitle = "The Ridge"));

        Assert.Equal(["Snowfall", "The Mill", "The Ridge"], manuscript.Chapters.Select(c => c.Title));
    }

    [Fact]
    public async Task Chapters_can_be_reordered_from_the_keyboard()
    {
        // The reorder that matters for SC-007: drag-and-drop cannot satisfy it, so the list exposes
        // move-up and move-down as commands a focused row can invoke.
        await using var app = new AppHarness();
        Manuscript created = (await app.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;
        await app.ChapterUseCases.AddAsync(created.Id, "Snowfall");
        await app.ChapterUseCases.AddAsync(created.Id, "The Mill");
        await app.ChapterUseCases.AddAsync(created.Id, "The Ridge");

        ManuscriptViewModel manuscript = app.Manuscript;
        manuscript.ManuscriptId = created.Id;
        await manuscript.LoadAsync();

        await AccessibilityHarness.WalkAsync(
            new KeyboardStep("move the last chapter up", manuscript.MoveChapterUpCommand, manuscript.Chapters[2]));
        await manuscript.LoadAsync();
        Assert.Equal(["Snowfall", "The Ridge", "The Mill"], manuscript.Chapters.Select(c => c.Title));

        await AccessibilityHarness.WalkAsync(
            new KeyboardStep("move the first chapter down", manuscript.MoveChapterDownCommand, manuscript.Chapters[0]));
        await manuscript.LoadAsync();
        Assert.Equal(["The Ridge", "Snowfall", "The Mill"], manuscript.Chapters.Select(c => c.Title));
    }

    [Fact]
    public async Task A_keyboard_reorder_survives_a_restart()
    {
        // SC-007 is only met if the keyboard route produces the same durable result as any other.
        await using var app = new AppHarness();
        Manuscript created = (await app.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;
        await app.ChapterUseCases.AddAsync(created.Id, "Snowfall");
        await app.ChapterUseCases.AddAsync(created.Id, "The Mill");

        ManuscriptViewModel manuscript = app.Manuscript;
        manuscript.ManuscriptId = created.Id;
        await manuscript.LoadAsync();
        await manuscript.MoveChapterUpCommand.ExecuteAsync(manuscript.Chapters[1]);

        await app.RestartAsync();

        ManuscriptViewModel reopened = app.Manuscript;
        reopened.ManuscriptId = created.Id;
        await reopened.LoadAsync();

        Assert.Equal(["The Mill", "Snowfall"], reopened.Chapters.Select(c => c.Title));
    }

    [Fact]
    public async Task Deleting_a_chapter_is_confirmed_and_reversible_by_declining_from_the_keyboard()
    {
        await using var app = new AppHarness();
        Manuscript created = (await app.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;
        await app.ChapterUseCases.AddAsync(created.Id, "Snowfall");

        ManuscriptViewModel manuscript = app.Manuscript;
        manuscript.ManuscriptId = created.Id;
        await manuscript.LoadAsync();

        app.Confirmation.NextAnswer = false;
        await AccessibilityHarness.WalkAsync(
            new KeyboardStep("delete the chapter", manuscript.DeleteChapterCommand, manuscript.Chapters[0]));
        Assert.Single(manuscript.Chapters);

        app.Confirmation.NextAnswer = true;
        await AccessibilityHarness.WalkAsync(
            new KeyboardStep("delete the chapter", manuscript.DeleteChapterCommand, manuscript.Chapters[0]));
        Assert.Empty(manuscript.Chapters);
    }

    // ---- Structure and outcomes announced in words (FR-019) ----

    [Fact]
    public async Task The_library_announces_its_structure_with_correct_grammar()
    {
        await using var app = new AppHarness();
        LibraryViewModel library = app.Library;

        await library.LoadAsync();
        Assert.Equal("No manuscripts yet.", library.StatusMessage);
        Assert.True(library.IsEmpty);

        library.NewManuscriptTitle = "The Long Winter";
        await library.CreateAsync();
        await library.LoadAsync();
        Assert.Equal("1 manuscript.", library.StatusMessage);
        Assert.False(library.IsEmpty);

        library.NewManuscriptTitle = "A Second Novel";
        await library.CreateAsync();
        await library.LoadAsync();
        Assert.Equal("2 manuscripts.", library.StatusMessage);
    }

    [Fact]
    public async Task A_manuscript_announces_its_chapter_count_and_word_count()
    {
        await using var app = new AppHarness();
        Manuscript created = (await app.ManuscriptUseCases.CreateAsync("The Long Winter")).Value;

        ManuscriptViewModel manuscript = app.Manuscript;
        manuscript.ManuscriptId = created.Id;

        await manuscript.LoadAsync();
        Assert.Equal("No chapters yet.", manuscript.StatusMessage);
        Assert.True(manuscript.IsEmpty);

        manuscript.NewChapterTitle = "Snowfall";
        await manuscript.AddChapterAsync();
        await manuscript.LoadAsync();
        Assert.Equal("1 chapter, 0 words.", manuscript.StatusMessage);

        manuscript.NewChapterTitle = "The Mill";
        await manuscript.AddChapterAsync();
        await manuscript.LoadAsync();
        Assert.Equal("2 chapters, 0 words.", manuscript.StatusMessage);
    }

    [Fact]
    public async Task Deleting_a_manuscript_names_what_will_be_lost_rather_than_asking_if_you_are_sure()
    {
        // "Are you sure?" is not informed consent — FR-005 wants the writer to know the cost.
        await using var app = new AppHarness();
        LibraryViewModel library = app.Library;
        library.NewManuscriptTitle = "The Long Winter";
        await library.CreateAsync();

        app.Confirmation.NextAnswer = false;
        await library.DeleteAsync(library.Manuscripts[0]);

        (string title, string message) = app.Confirmation.Prompts.Single();
        Assert.Equal("Delete manuscript?", title);
        Assert.Contains("The Long Winter", message, StringComparison.Ordinal);
        Assert.Contains("0 chapters", message, StringComparison.Ordinal);
        Assert.Contains("cannot be undone", message, StringComparison.Ordinal);
        Assert.Equal("Nothing was deleted.", library.StatusMessage);
    }

    [Fact]
    public async Task Save_state_is_reported_as_text_rather_than_as_an_appearance()
    {
        // FR-019: no status by colour alone. The editor's save state has to be readable.
        await using var app = new AppHarness(autoSaveDebounce: TimeSpan.FromMilliseconds(20));
        (_, Guid chapterId) = await app.OpenNewChapterAsync();

        app.EditorHost.Type(chapterId, "Elin watched the mill burn from the ridge.");
        await app.Editor.FlushAsync();

        Assert.False(string.IsNullOrWhiteSpace(app.Editor.StatusMessage));
        Assert.Contains("Saved", app.Editor.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_image_without_alternative_text_is_flagged_in_words_and_not_blocked()
    {
        // The spec's edge case: permitted, but never silently accepted as compliant.
        await using var app = new AppHarness();
        (_, Guid chapterId) = await app.OpenNewChapterAsync();

        app.EditorHost.PasteImage(chapterId, [0x89, 0x50, 0x4E, 0x47], "image/png", altText: null);
        await Task.Delay(50);

        InlineImageReference inserted = Assert.Single(app.EditorHost.InsertedImages);
        Assert.True(inserted.IsMissingAltText);
        Assert.True(app.Editor.HasImagesNeedingAltText);
        Assert.Equal("1 image still needs alternative text for screen readers.", app.Editor.AltTextGapSummary);
    }

    // ---- Contrast (WCAG 2.1 SC 1.4.3 and 1.4.11) ----

    [Theory]
    [InlineData("TextPrimaryLight", "PageBackgroundLight")]
    [InlineData("TextPrimaryLight", "SurfaceLight")]
    [InlineData("TextSecondaryLight", "PageBackgroundLight")]
    [InlineData("TextSecondaryLight", "SurfaceLight")]
    [InlineData("TextPrimaryDark", "PageBackgroundDark")]
    [InlineData("TextPrimaryDark", "SurfaceDark")]
    [InlineData("TextSecondaryDark", "PageBackgroundDark")]
    [InlineData("TextSecondaryDark", "SurfaceDark")]
    public void Body_text_meets_AA_contrast_in_both_themes(string foreground, string background)
    {
        IReadOnlyDictionary<string, Rgb> palette = AccessibilityHarness.LoadPalette();

        double ratio = AccessibilityHarness.ContrastRatio(palette[foreground], palette[background]);

        Assert.True(
            ratio >= AccessibilityHarness.AaBodyText,
            $"{foreground} on {background} is {ratio:F2}:1, below the {AccessibilityHarness.AaBodyText}:1 AA minimum for body text.");
    }

    [Theory]
    [InlineData("AccentLight", "PageBackgroundLight")]
    [InlineData("SuccessLight", "PageBackgroundLight")]
    [InlineData("WarningLight", "PageBackgroundLight")]
    [InlineData("DangerLight", "PageBackgroundLight")]
    [InlineData("AccentDark", "SurfaceDark")]
    [InlineData("SuccessDark", "SurfaceDark")]
    [InlineData("WarningDark", "SurfaceDark")]
    [InlineData("DangerDark", "SurfaceDark")]
    public void Status_and_accent_text_meets_AA_contrast_in_both_themes(string foreground, string background)
    {
        // These carry words, not just colour (FR-019), so they are held to the body-text ratio.
        IReadOnlyDictionary<string, Rgb> palette = AccessibilityHarness.LoadPalette();

        double ratio = AccessibilityHarness.ContrastRatio(palette[foreground], palette[background]);

        Assert.True(
            ratio >= AccessibilityHarness.AaBodyText,
            $"{foreground} on {background} is {ratio:F2}:1, below the {AccessibilityHarness.AaBodyText}:1 AA minimum.");
    }

    [Theory]
    [InlineData("ControlBorderLight", "PageBackgroundLight")]
    [InlineData("ControlBorderLight", "SurfaceLight")]
    [InlineData("FocusRingLight", "PageBackgroundLight")]
    [InlineData("ControlBorderDark", "SurfaceDark")]
    [InlineData("ControlBorderDark", "PageBackgroundDark")]
    [InlineData("FocusRingDark", "SurfaceDark")]
    public void Control_boundaries_and_focus_rings_meet_the_AA_non_text_ratio(string foreground, string background)
    {
        // SC 1.4.11. These strokes outline the transparent secondary/danger buttons and the text
        // entries — remove them and the control is invisible — so the boundary carries information
        // and has to clear 3:1. The decorative Border* token deliberately does not, and is not
        // asserted here; the test below pins which token each role uses so the two cannot be swapped.
        IReadOnlyDictionary<string, Rgb> palette = AccessibilityHarness.LoadPalette();

        double ratio = AccessibilityHarness.ContrastRatio(palette[foreground], palette[background]);

        Assert.True(
            ratio >= AccessibilityHarness.AaLargeTextAndUi,
            $"{foreground} on {background} is {ratio:F2}:1, below the {AccessibilityHarness.AaLargeTextAndUi}:1 AA minimum for UI boundaries.");
    }

    [Fact]
    public void A_control_whose_only_boundary_is_its_stroke_uses_the_contrast_checked_token()
    {
        // The exemption for the decorative token is only sound while nothing load-bearing uses it.
        // If a future style gives a transparent control the soft Border token, the contrast theory
        // above would still pass while the control quietly became invisible; this is what catches it.
        string styles = File.ReadAllText(AccessibilityHarness.PathToMauiFile("Resources", "Styles", "Styles.xaml"));

        int secondaryButton = styles.IndexOf("x:Key=\"SecondaryButton\"", StringComparison.Ordinal);
        Assert.True(secondaryButton >= 0, "The SecondaryButton style has gone; this test needs updating.");

        string styleBody = styles[secondaryButton..styles.IndexOf("</Style>", secondaryButton, StringComparison.Ordinal)];

        Assert.Contains("ControlBorderLight", styleBody, StringComparison.Ordinal);
        Assert.Contains("ControlBorderDark", styleBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_theme_token_has_a_counterpart_in_the_other_theme()
    {
        // A token added to one theme only is a screen that is unreadable in the other.
        IReadOnlyDictionary<string, Rgb> palette = AccessibilityHarness.LoadPalette();

        string[] unpaired =
        [
            .. palette.Keys
                .Where(key => key.EndsWith("Light", StringComparison.Ordinal))
                .Select(key => key[..^"Light".Length])
                .Where(stem => !palette.ContainsKey(stem + "Dark"))
        ];

        Assert.Empty(unpaired);
    }
}
