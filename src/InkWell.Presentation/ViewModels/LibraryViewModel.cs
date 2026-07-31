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
/// The library: every manuscript on this device (FR-001).
/// </summary>
/// <remarks>
/// Depends only on use cases and the prompt/navigation interfaces, never on a <c>Page</c>, so the
/// whole screen — including its confirm-before-delete behaviour — is driven headlessly by story
/// tests.
/// </remarks>
public sealed partial class LibraryViewModel : BaseViewModel
{
    private readonly ManuscriptUseCases _manuscripts;
    private readonly INavigationService _navigation;
    private readonly IConfirmationService _confirmation;
    private readonly IErrorPresenter _errors;

    /// <summary>Creates the view model.</summary>
    public LibraryViewModel(
        ManuscriptUseCases manuscripts,
        INavigationService navigation,
        IConfirmationService confirmation,
        IErrorPresenter errors)
    {
        _manuscripts = manuscripts;
        _navigation = navigation;
        _confirmation = confirmation;
        _errors = errors;
        Title = "Your manuscripts";
    }

    /// <summary>Every manuscript, newest-modified first.</summary>
    public ObservableCollection<ManuscriptSummary> Manuscripts { get; } = [];

    /// <summary>The title typed into the "new manuscript" field.</summary>
    [ObservableProperty]
    public partial string NewManuscriptTitle { get; set; } = string.Empty;

    /// <summary>
    /// True when the writer has nothing yet, so the page can offer guidance instead of a blank
    /// screen (spec.md edge case "empty states").
    /// </summary>
    public bool IsEmpty => Manuscripts.Count == 0 && !IsBusy;

    /// <summary>Loads the library.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            IReadOnlyList<ManuscriptSummary> all = await _manuscripts.ListAsync().ConfigureAwait(true);
            Manuscripts.Clear();
            foreach (ManuscriptSummary summary in all)
            {
                Manuscripts.Add(summary);
            }

            StatusMessage = Manuscripts.Count switch
            {
                0 => "No manuscripts yet.",
                1 => "1 manuscript.",
                _ => $"{Manuscripts.Count} manuscripts.",
            };
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Creates a manuscript from <see cref="NewManuscriptTitle"/> (US1 scenario 1).</summary>
    [RelayCommand]
    public async Task CreateAsync()
    {
        DomainResult<Manuscript> created = await _manuscripts.CreateAsync(NewManuscriptTitle).ConfigureAwait(true);
        if (created.IsFailure)
        {
            await _errors.ShowAsync("That title will not work", created.Error.Message).ConfigureAwait(true);
            return;
        }

        NewManuscriptTitle = string.Empty;
        await LoadAsync().ConfigureAwait(true);
        StatusMessage = $"Created “{created.Value.Title}”.";
    }

    /// <summary>Opens a manuscript.</summary>
    [RelayCommand]
    public Task OpenAsync(ManuscriptSummary? manuscript)
        => manuscript is null
            ? Task.CompletedTask
            : _navigation.GoToAsync(Routes.Manuscript, new Dictionary<string, object>
            {
                [Routes.ManuscriptIdParameter] = manuscript.Id,
            });

    /// <summary>Renames a manuscript.</summary>
    [RelayCommand]
    public async Task RenameAsync((Guid Id, string Title) request)
    {
        DomainResult result = await _manuscripts.RenameAsync(request.Id, request.Title).ConfigureAwait(true);
        if (result.IsFailure)
        {
            await _errors.ShowAsync("That title will not work", result.Error.Message).ConfigureAwait(true);
            return;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Deletes a manuscript, but only after the writer confirms (FR-005). The confirmation names
    /// exactly what will be lost, because "are you sure?" is not informed consent.
    /// </summary>
    [RelayCommand]
    public async Task DeleteAsync(ManuscriptSummary? manuscript)
    {
        if (manuscript is null)
        {
            return;
        }

        bool confirmed = await _confirmation.ConfirmDestructiveAsync(
            "Delete manuscript?",
            $"“{manuscript.Title}” and its {Describe(manuscript.ChapterCount, "chapter")} " +
            $"({manuscript.WordCount:N0} words), along with its characters, plot threads, goal, and " +
            "writing history, will be permanently deleted from this device. This cannot be undone.",
            "Delete").ConfigureAwait(true);

        if (!confirmed)
        {
            StatusMessage = "Nothing was deleted.";
            return;
        }

        DomainResult result = await _manuscripts.DeleteAsync(manuscript.Id).ConfigureAwait(true);
        if (result.IsFailure)
        {
            await _errors.ShowAsync("Could not delete", result.Error.Message).ConfigureAwait(true);
            return;
        }

        await LoadAsync().ConfigureAwait(true);
        StatusMessage = $"Deleted “{manuscript.Title}”.";
    }

    private static string Describe(int count, string noun)
        => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
