using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Application.UseCases;
using InkWell.Domain.Abstractions;
using InkWell.Presentation.Controls;
using InkWell.Presentation.Services;

namespace InkWell.Presentation.ViewModels;

/// <summary>
/// The chapter editor: loads one chapter, autosaves it, and reports live counts (FR-003, FR-004).
/// </summary>
public sealed partial class EditorViewModel : BaseViewModel, IQueryAttributable, IAsyncDisposable
{
    private readonly ChapterUseCases _chapters;
    private readonly GoalUseCases _goals;
    private readonly IInlineImageRepository _images;
    private readonly AutoSaveCoordinator _autoSave;
    private readonly IClock _clock;
    private readonly INavigationService _navigation;
    private readonly IErrorPresenter _errors;

    private IEditorHost? _host;

    /// <summary>Creates the view model.</summary>
    public EditorViewModel(
        ChapterUseCases chapters,
        GoalUseCases goals,
        IInlineImageRepository images,
        AutoSaveCoordinator autoSave,
        IClock clock,
        INavigationService navigation,
        IErrorPresenter errors)
    {
        _chapters = chapters;
        _goals = goals;
        _images = images;
        _autoSave = autoSave;
        _clock = clock;
        _navigation = navigation;
        _errors = errors;

        _autoSave.Saved += OnSaved;
        _autoSave.SaveFailed += OnSaveFailed;
    }

    /// <summary>The chapter being edited.</summary>
    [ObservableProperty]
    public partial Guid ChapterId { get; set; }

    /// <summary>The manuscript it belongs to.</summary>
    [ObservableProperty]
    public partial Guid ManuscriptId { get; set; }

    /// <summary>This chapter's live prose word count (FR-009).</summary>
    [ObservableProperty]
    public partial int ChapterWordCount { get; set; }

    /// <summary>The whole manuscript's live prose word count (FR-009).</summary>
    [ObservableProperty]
    public partial int ManuscriptWordCount { get; set; }

    /// <summary>Whether the chrome-free writing mode is active (FR-007).</summary>
    [ObservableProperty]
    public partial bool IsDistractionFree { get; set; }

    /// <summary>
    /// How many images in this chapter still lack alternative text. Surfaced as a count with a text
    /// label, never as a colour, so the gap is perceivable to everyone (FR-019).
    /// </summary>
    [ObservableProperty]
    public partial int ImagesNeedingAltText { get; set; }

    /// <summary>
    /// Today's progress toward the daily goal, refreshed on every save (FR-011, US3 scenario 2).
    /// </summary>
    [ObservableProperty]
    public partial DailyProgress? TodayProgress { get; set; }

    /// <summary>The word-count line the view shows and the screen reader announces.</summary>
    public string CountsSummary =>
        $"{ChapterWordCount:N0} words in this chapter · {ManuscriptWordCount:N0} in the manuscript";

    /// <summary>
    /// The daily-goal line, written out in full — words so far, target, words remaining, and the
    /// status named in text so nothing depends on colour (FR-019).
    /// </summary>
    public string GoalSummary => TodayProgress?.Summary ?? string.Empty;

    /// <summary>Whether a daily goal is being tracked, so the view can omit the line entirely.</summary>
    public bool HasDailyGoal => TodayProgress?.Target is > 0;

    partial void OnTodayProgressChanged(DailyProgress? value)
    {
        OnPropertyChanged(nameof(GoalSummary));
        OnPropertyChanged(nameof(HasDailyGoal));
    }

    /// <summary>Whether any image in this chapter still lacks alternative text.</summary>
    public bool HasImagesNeedingAltText => ImagesNeedingAltText > 0;

    /// <summary>The accessibility-gap line, written out in words rather than shown as an icon.</summary>
    public string AltTextGapSummary => ImagesNeedingAltText == 1
        ? "1 image still needs alternative text for screen readers."
        : $"{ImagesNeedingAltText} images still need alternative text for screen readers.";

    partial void OnImagesNeedingAltTextChanged(int value)
    {
        OnPropertyChanged(nameof(HasImagesNeedingAltText));
        OnPropertyChanged(nameof(AltTextGapSummary));
    }

    /// <inheritdoc />
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.TryGetValue(Routes.ChapterIdParameter, out object? chapter) && chapter is Guid chapterId)
        {
            ChapterId = chapterId;
        }

        if (query.TryGetValue(Routes.ManuscriptIdParameter, out object? manuscript) && manuscript is Guid manuscriptId)
        {
            ManuscriptId = manuscriptId;
        }
    }

    /// <summary>Connects the view model to its editor surface.</summary>
    public void Attach(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Detach();

        _host = host;
        host.ContentChanged += OnContentChanged;
        host.FlushRequested += OnFlushRequested;
        host.ImageRequested += OnImageRequested;
        host.ImageMissingAltText += OnImageMissingAltText;
        host.DistractionFreeToggleRequested += OnDistractionFreeToggleRequested;
        host.BridgeFailed += OnBridgeFailed;
    }

    /// <summary>Disconnects from the editor surface.</summary>
    public void Detach()
    {
        if (_host is null)
        {
            return;
        }

        _host.ContentChanged -= OnContentChanged;
        _host.FlushRequested -= OnFlushRequested;
        _host.ImageRequested -= OnImageRequested;
        _host.ImageMissingAltText -= OnImageMissingAltText;
        _host.DistractionFreeToggleRequested -= OnDistractionFreeToggleRequested;
        _host.BridgeFailed -= OnBridgeFailed;
        _host = null;
    }

    /// <summary>Loads the chapter into the editor.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            DomainResult<ChapterContent> content = await _chapters.GetContentAsync(ChapterId).ConfigureAwait(true);
            if (content.IsFailure)
            {
                await _errors.ShowAsync("Could not open chapter", content.Error.Message).ConfigureAwait(true);
                return;
            }

            Title = content.Value.Title;
            ManuscriptId = content.Value.ManuscriptId;
            ChapterWordCount = Domain.Services.ProseWordCounter.Count(content.Value.ContentMarkdown);
            ManuscriptWordCount = await _chapters.GetManuscriptWordCountAsync(ManuscriptId).ConfigureAwait(true);
            ImagesNeedingAltText = content.Value.Images.Count(i => i.IsMissingAltText);
            TodayProgress = await _goals.GetTodayProgressAsync(ManuscriptId).ConfigureAwait(true);

            if (_host is not null)
            {
                await _host.LoadChapterAsync(content.Value).ConfigureAwait(true);
            }

            StatusMessage = "Saved";
            OnPropertyChanged(nameof(CountsSummary));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Commits any pending edit right now. Called on navigation away, focus loss, app suspend, and
    /// every distraction-free toggle, so a shutdown can only ever cost the last moments (SC-003).
    /// </summary>
    [RelayCommand]
    public async Task FlushAsync()
    {
        AutoSaveResult? result = await _autoSave.FlushAsync().ConfigureAwait(true);
        if (result is not null)
        {
            Apply(result);
        }
    }

    /// <summary>
    /// Opens the character list beside the editor and comes back to the exact caret (FR-015).
    /// </summary>
    [RelayCommand]
    public Task OpenCharactersAsync() => OpenReferenceAsync(Routes.Characters);

    /// <summary>Opens the plot-thread list and comes back to the exact caret (FR-015).</summary>
    [RelayCommand]
    public Task OpenPlotThreadsAsync() => OpenReferenceAsync(Routes.PlotThreads);

    /// <summary>
    /// Restores the writer's place after a reference view closes.
    /// </summary>
    /// <remarks>
    /// Called when the editor page reappears. The editor's document and selection were never
    /// touched — looking something up is not an edit — so returning to the exact caret is a matter
    /// of handing focus back rather than of restoring saved state (US4 scenario 4).
    /// </remarks>
    [RelayCommand]
    public async Task ResumeWritingAsync()
    {
        if (_host is not null)
        {
            await _host.FocusAsync().ConfigureAwait(true);
        }

        StatusMessage = "Back to your chapter.";
    }

    /// <summary>Enters or leaves the chrome-free writing mode (FR-007, FR-008).</summary>
    [RelayCommand]
    public async Task ToggleDistractionFreeAsync()
    {
        // Flush before the transition so the mode change can never be the thing that loses a
        // sentence, and so the two modes are indistinguishable from the store's point of view.
        await FlushAsync().ConfigureAwait(true);

        IsDistractionFree = !IsDistractionFree;

        if (_host is not null)
        {
            await _host.SetDistractionFreeAsync(IsDistractionFree).ConfigureAwait(true);

            // The caret is restored explicitly rather than left to the layout change, which is what
            // makes "return to their exact cursor position" true on all three WebView engines.
            await _host.FocusAsync().ConfigureAwait(true);
        }

        StatusMessage = IsDistractionFree
            ? "Distraction-free mode on. Press Escape to leave."
            : "Distraction-free mode off.";
    }

    private async Task OpenReferenceAsync(string route)
    {
        // Committed before leaving, so a reference lookup can never be the thing that loses a
        // sentence — the same rule as the distraction-free transition (FR-004).
        await FlushAsync().ConfigureAwait(true);

        await _navigation.GoToAsync(route, new Dictionary<string, object>
        {
            [Routes.ManuscriptIdParameter] = ManuscriptId,
        }).ConfigureAwait(true);
    }

    private void OnContentChanged(object? sender, EditorContentChanged e)
        => _autoSave.QueueEdit(e.ChapterId == Guid.Empty ? ChapterId : e.ChapterId, e.Markdown);

    private async void OnFlushRequested(object? sender, EventArgs e) => await FlushAsync().ConfigureAwait(true);

    private async void OnDistractionFreeToggleRequested(object? sender, EventArgs e)
        => await ToggleDistractionFreeAsync().ConfigureAwait(true);

    private async void OnBridgeFailed(object? sender, string reason)
    {
        // The editor is unreachable, so nothing is being autosaved. Saying so is the whole point:
        // a writer who is told immediately loses a paragraph, one who is not loses an evening.
        StatusMessage = "Not saved — the writing surface did not load";
        await _errors.ShowAsync("Your typing is not being saved", reason).ConfigureAwait(true);
    }

    private async void OnImageRequested(object? sender, EditorImageRequested e)
    {
        try
        {
            InlineImageReference reference = await _images
                .AddAsync(new InlineImageInsert(ChapterId, e.Bytes, e.MimeType, e.AltText), _clock.Now)
                .ConfigureAwait(true);

            if (_host is not null)
            {
                await _host.InsertImageAsync(reference).ConfigureAwait(true);
            }

            if (reference.IsMissingAltText)
            {
                ImagesNeedingAltText++;
                StatusMessage = "Image added. It still needs alternative text for screen readers.";
            }
            else
            {
                StatusMessage = "Image added.";
            }
        }
        catch (ArgumentException ex)
        {
            await _errors.ShowAsync("Could not add that image", ex.Message).ConfigureAwait(true);
        }
    }

    private void OnImageMissingAltText(object? sender, Guid imageId) => ImagesNeedingAltText++;

    private void OnSaved(object? sender, AutoSaveResult result) => Apply(result);

    private async void OnSaveFailed(object? sender, Exception error)
    {
        StatusMessage = "Not saved";
        await _errors.ShowAsync(
            "Your last edit was not saved",
            "InkWell could not write to the manuscript store on this device. Your text is still on " +
            $"screen — copy it somewhere safe before closing the app.\n\n{error.Message}").ConfigureAwait(true);
    }

    private void Apply(AutoSaveResult result)
    {
        ChapterWordCount = result.ChapterWordCount;
        ManuscriptWordCount = result.ManuscriptWordCount;

        // Built from what the commit already returned rather than re-queried: this runs on every
        // autosave, and a database round trip here would be work on the typing path (FR-011).
        TodayProgress = GoalUseCases.ProgressFrom(result.WordsWrittenToday, result.DailyGoalTarget);

        StatusMessage = "Saved";
        OnPropertyChanged(nameof(CountsSummary));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Detach();
        _autoSave.Saved -= OnSaved;
        _autoSave.SaveFailed -= OnSaveFailed;
        await _autoSave.DisposeAsync().ConfigureAwait(false);
    }
}
