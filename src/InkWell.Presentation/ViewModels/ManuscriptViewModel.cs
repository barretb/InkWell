using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Application.UseCases;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;
using InkWell.Presentation.Services;

namespace InkWell.Presentation.ViewModels;

/// <summary>
/// One manuscript and its chapters: add, rename, reorder, delete, open (FR-002).
/// </summary>
public sealed partial class ManuscriptViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ManuscriptUseCases _manuscripts;
    private readonly ChapterUseCases _chapters;
    private readonly INavigationService _navigation;
    private readonly IConfirmationService _confirmation;
    private readonly IErrorPresenter _errors;

    /// <summary>Creates the view model.</summary>
    public ManuscriptViewModel(
        ManuscriptUseCases manuscripts,
        ChapterUseCases chapters,
        INavigationService navigation,
        IConfirmationService confirmation,
        IErrorPresenter errors)
    {
        _manuscripts = manuscripts;
        _chapters = chapters;
        _navigation = navigation;
        _confirmation = confirmation;
        _errors = errors;
    }

    /// <summary>The manuscript being shown.</summary>
    [ObservableProperty]
    public partial Guid ManuscriptId { get; set; }

    /// <summary>Its chapters, in order.</summary>
    public ObservableCollection<ChapterSummary> Chapters { get; } = [];

    /// <summary>The title typed into the "new chapter" field.</summary>
    [ObservableProperty]
    public partial string NewChapterTitle { get; set; } = string.Empty;

    /// <summary>The manuscript's total prose word count (FR-009).</summary>
    [ObservableProperty]
    public partial int WordCount { get; set; }

    /// <summary>True when the manuscript has no chapters yet.</summary>
    public bool IsEmpty => Chapters.Count == 0 && !IsBusy;

    /// <inheritdoc />
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TryGetValue(Routes.ManuscriptIdParameter, out object? value) && value is Guid id)
        {
            ManuscriptId = id;
        }
    }

    /// <summary>Loads the manuscript and its chapter list.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            DomainResult<ManuscriptDetail> detail = await _manuscripts.GetAsync(ManuscriptId).ConfigureAwait(true);
            if (detail.IsFailure)
            {
                await _errors.ShowAsync("Could not open", detail.Error.Message).ConfigureAwait(true);
                await _navigation.GoToAsync(Routes.Library).ConfigureAwait(true);
                return;
            }

            Title = detail.Value.Title;
            WordCount = detail.Value.WordCount;

            Chapters.Clear();
            foreach (ChapterSummary chapter in detail.Value.Chapters)
            {
                Chapters.Add(chapter);
            }

            StatusMessage = Chapters.Count == 0
                ? "No chapters yet."
                : $"{Describe(Chapters.Count, "chapter")}, {WordCount:N0} words.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Appends a chapter to the end of the manuscript.</summary>
    [RelayCommand]
    public async Task AddChapterAsync()
    {
        DomainResult<Chapter> added = await _chapters.AddAsync(ManuscriptId, NewChapterTitle).ConfigureAwait(true);
        if (added.IsFailure)
        {
            await _errors.ShowAsync("That title will not work", added.Error.Message).ConfigureAwait(true);
            return;
        }

        NewChapterTitle = string.Empty;
        await LoadAsync().ConfigureAwait(true);
        StatusMessage = $"Added “{added.Value.Title}”.";
    }

    /// <summary>Opens a chapter in the editor.</summary>
    [RelayCommand]
    public Task OpenChapterAsync(ChapterSummary? chapter)
        => chapter is null
            ? Task.CompletedTask
            : _navigation.GoToAsync(Routes.Editor, new Dictionary<string, object>
            {
                [Routes.ChapterIdParameter] = chapter.Id,
                [Routes.ManuscriptIdParameter] = ManuscriptId,
            });

    /// <summary>Opens the daily goal and writing history for this manuscript.</summary>
    [RelayCommand]
    public Task OpenGoalsAsync()
        => _navigation.GoToAsync(Routes.Goals, new Dictionary<string, object>
        {
            [Routes.ManuscriptIdParameter] = ManuscriptId,
        });

    /// <summary>Renames a chapter.</summary>
    [RelayCommand]
    public async Task RenameChapterAsync((Guid Id, string Title) request)
    {
        DomainResult result = await _chapters.RenameAsync(request.Id, request.Title).ConfigureAwait(true);
        if (result.IsFailure)
        {
            await _errors.ShowAsync("That title will not work", result.Error.Message).ConfigureAwait(true);
            return;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Moves a chapter one place earlier. Bound to a button and to a keyboard accelerator so that
    /// reordering never requires a pointer (FR-019, SC-007).
    /// </summary>
    [RelayCommand]
    public Task MoveChapterUpAsync(ChapterSummary? chapter) => MoveAsync(chapter, -1);

    /// <summary>Moves a chapter one place later.</summary>
    [RelayCommand]
    public Task MoveChapterDownAsync(ChapterSummary? chapter) => MoveAsync(chapter, +1);

    /// <summary>Deletes a chapter after the writer confirms (FR-005).</summary>
    [RelayCommand]
    public async Task DeleteChapterAsync(ChapterSummary? chapter)
    {
        if (chapter is null)
        {
            return;
        }

        bool confirmed = await _confirmation.ConfirmDestructiveAsync(
            "Delete chapter?",
            $"“{chapter.Title}” and its {chapter.WordCount:N0} words will be permanently deleted " +
            "from this device. This cannot be undone.",
            "Delete").ConfigureAwait(true);

        if (!confirmed)
        {
            StatusMessage = "Nothing was deleted.";
            return;
        }

        DomainResult result = await _chapters.DeleteAsync(chapter.Id).ConfigureAwait(true);
        if (result.IsFailure)
        {
            await _errors.ShowAsync("Could not delete", result.Error.Message).ConfigureAwait(true);
            return;
        }

        await LoadAsync().ConfigureAwait(true);
        StatusMessage = $"Deleted “{chapter.Title}”.";
    }

    private async Task MoveAsync(ChapterSummary? chapter, int offset)
    {
        if (chapter is null)
        {
            return;
        }

        DomainResult result = await _chapters.MoveAsync(ManuscriptId, chapter.Id, offset).ConfigureAwait(true);
        if (result.IsFailure)
        {
            await _errors.ShowAsync("Could not reorder", result.Error.Message).ConfigureAwait(true);
            return;
        }

        await LoadAsync().ConfigureAwait(true);

        int position = Chapters.ToList().FindIndex(c => c.Id == chapter.Id);
        // Announced as text so a screen-reader user hears the outcome of the move.
        StatusMessage = $"“{chapter.Title}” is now chapter {position + 1} of {Chapters.Count}.";
    }

    private static string Describe(int count, string noun)
        => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
