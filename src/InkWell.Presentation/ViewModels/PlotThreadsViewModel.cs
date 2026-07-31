using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InkWell.Application.UseCases;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;
using InkWell.Presentation.Services;

namespace InkWell.Presentation.ViewModels;

/// <summary>
/// Plot threads for one manuscript (FR-014). Edits save themselves; see
/// <see cref="CharactersViewModel"/> for why there is no save button.
/// </summary>
public sealed partial class PlotThreadsViewModel : BaseViewModel, IQueryAttributable, IAsyncDisposable
{
    private readonly ReferenceUseCases _references;
    private readonly IConfirmationService _confirmation;
    private readonly IErrorPresenter _errors;
    private readonly Debouncer _autoSave;

    /// <summary>Creates the view model.</summary>
    public PlotThreadsViewModel(
        ReferenceUseCases references,
        IConfirmationService confirmation,
        IErrorPresenter errors,
        Debouncer? autoSave = null)
    {
        _references = references;
        _confirmation = confirmation;
        _errors = errors;
        _autoSave = autoSave ?? new Debouncer();
        _autoSave.Failed += OnAutoSaveFailed;
        Title = "Plot threads";
    }

    /// <summary>The manuscript these threads belong to.</summary>
    [ObservableProperty]
    public partial Guid ManuscriptId { get; set; }

    /// <summary>The plot threads, title-sorted, each editing itself in place.</summary>
    public ObservableCollection<PlotThreadEditor> PlotThreads { get; } = [];

    /// <summary>The title typed into the new-thread field.</summary>
    [ObservableProperty]
    public partial string NewTitle { get; set; } = string.Empty;

    /// <summary>The notes typed into the new-thread field.</summary>
    [ObservableProperty]
    public partial string NewNotes { get; set; } = string.Empty;

    /// <summary>True when there are no plot threads yet.</summary>
    public bool IsEmpty => PlotThreads.Count == 0 && !IsBusy;

    /// <inheritdoc />
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TryGetValue(Routes.ManuscriptIdParameter, out object? value) && value is Guid id)
        {
            ManuscriptId = id;
        }
    }

    /// <summary>Loads the manuscript's plot threads.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            foreach (PlotThreadEditor existing in PlotThreads)
            {
                existing.Edited -= OnPlotThreadEdited;
            }

            PlotThreads.Clear();
            foreach (PlotThread thread in await _references.ListPlotThreadsAsync(ManuscriptId).ConfigureAwait(true))
            {
                var editor = new PlotThreadEditor(thread);
                editor.Edited += OnPlotThreadEdited;
                PlotThreads.Add(editor);
            }

            StatusMessage = PlotThreads.Count switch
            {
                0 => "No plot threads yet.",
                1 => "1 plot thread.",
                _ => $"{PlotThreads.Count} plot threads.",
            };
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Adds a plot thread (US4 scenario 2).</summary>
    [RelayCommand]
    public async Task CreateAsync()
    {
        DomainResult<PlotThread> created = await _references
            .CreatePlotThreadAsync(ManuscriptId, NewTitle, NewNotes)
            .ConfigureAwait(true);

        if (created.IsFailure)
        {
            await _errors.ShowAsync("That plot thread will not save", created.Error.Message).ConfigureAwait(true);
            return;
        }

        NewTitle = string.Empty;
        NewNotes = string.Empty;
        await LoadAsync().ConfigureAwait(true);
        StatusMessage = $"Added “{created.Value.Title}”.";
    }

    /// <summary>Writes any pending edit immediately, for when the screen goes away.</summary>
    [RelayCommand]
    public Task FlushAsync() => _autoSave.FlushAsync();

    /// <summary>Deletes a plot thread after the writer confirms (FR-005).</summary>
    [RelayCommand]
    public async Task DeleteAsync(PlotThreadEditor? thread)
    {
        if (thread is null)
        {
            return;
        }

        bool confirmed = await _confirmation.ConfirmDestructiveAsync(
            "Delete plot thread?",
            $"“{thread.Title}” and its notes will be permanently deleted. Your chapters are not " +
            "changed — anything you wrote about this thread stays exactly as you wrote it.",
            "Delete").ConfigureAwait(true);

        if (!confirmed)
        {
            StatusMessage = "Nothing was deleted.";
            return;
        }

        DomainResult result = await _references.DeletePlotThreadAsync(thread.Id).ConfigureAwait(true);
        if (result.IsFailure)
        {
            await _errors.ShowAsync("Could not delete", result.Error.Message).ConfigureAwait(true);
            return;
        }

        string title = thread.Title;
        await LoadAsync().ConfigureAwait(true);
        StatusMessage = $"Deleted “{title}”.";
    }

    private void OnPlotThreadEdited(object? sender, EventArgs e)
    {
        if (sender is not PlotThreadEditor editor)
        {
            return;
        }

        StatusMessage = "Saving…";
        _autoSave.Schedule(async () =>
        {
            DomainResult result = await _references
                .UpdatePlotThreadAsync(editor.Id, editor.Title, editor.Notes)
                .ConfigureAwait(false);

            StatusMessage = result.IsSuccess
                ? "Saved"
                : result.Error.Code == DomainErrorCode.ValidationError
                    ? "Not saved — a title is required"
                    : "Not saved";
        });
    }

    private async void OnAutoSaveFailed(object? sender, Exception error)
        => await _errors.ShowAsync("A change was not saved", error.Message).ConfigureAwait(true);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _autoSave.Failed -= OnAutoSaveFailed;
        await _autoSave.DisposeAsync().ConfigureAwait(false);
    }
}
