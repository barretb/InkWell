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
/// The daily word-count goal: set it, change it, clear it, and see progress and history
/// (FR-010, FR-011, FR-012).
/// </summary>
/// <remarks>
/// The target saves itself as it is typed. There is no save button on this screen for the same
/// reason there is none in the editor (FR-004).
/// </remarks>
public sealed partial class GoalsViewModel : BaseViewModel, IQueryAttributable, IAsyncDisposable
{
    private readonly GoalUseCases _goals;
    private readonly IConfirmationService _confirmation;
    private readonly IErrorPresenter _errors;
    private readonly Debouncer _autoSave;
    private bool _loading;

    /// <summary>Creates the view model.</summary>
    public GoalsViewModel(
        GoalUseCases goals,
        IConfirmationService confirmation,
        IErrorPresenter errors,
        Debouncer? autoSave = null)
    {
        _goals = goals;
        _confirmation = confirmation;
        _errors = errors;
        _autoSave = autoSave ?? new Debouncer();
        _autoSave.Failed += OnAutoSaveFailed;
        Title = "Daily goal";
    }

    /// <summary>The manuscript whose goal is shown.</summary>
    [ObservableProperty]
    public partial Guid ManuscriptId { get; set; }

    /// <summary>Today's progress.</summary>
    [ObservableProperty]
    public partial DailyProgress? Progress { get; set; }

    /// <summary>The target typed into the entry field, as text so an empty box is a valid state.</summary>
    [ObservableProperty]
    public partial string TargetInput { get; set; } = string.Empty;

    /// <summary>Prior days, newest first.</summary>
    public ObservableCollection<WritingHistoryEntry> History { get; } = [];

    /// <summary>Whether a goal is currently being tracked.</summary>
    public bool HasActiveGoal => Progress?.Target is > 0;

    /// <summary>
    /// The whole progress statement, spoken by a screen reader and shown on screen. Everything the
    /// writer needs — words so far, the target, what remains, and the state — is in this one
    /// sentence, because a progress bar alone would convey none of it (FR-019, SC-007).
    /// </summary>
    public string ProgressSummary => Progress?.Summary ?? "No daily goal set yet.";

    /// <summary>The status on its own, for the line beneath the bar.</summary>
    public string StatusText => Progress?.StatusText ?? "No daily goal set";

    /// <summary>Completion as a fraction, capped at 1.0; zero when no goal is set.</summary>
    public double ProgressFraction => Progress?.Fraction ?? 0d;

    /// <summary>True when there is no history yet, so the page can explain rather than show a void.</summary>
    public bool HasNoHistory => History.Count == 0;

    /// <inheritdoc />
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TryGetValue(Routes.ManuscriptIdParameter, out object? value) && value is Guid id)
        {
            ManuscriptId = id;
        }
    }

    /// <summary>Loads today's progress and the recent history.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        _loading = true;
        try
        {
            Progress = await _goals.GetTodayProgressAsync(ManuscriptId).ConfigureAwait(true);

            DailyGoal? goal = await _goals.GetGoalAsync(ManuscriptId).ConfigureAwait(true);
            TargetInput = goal is { TargetWords: > 0 } ? goal.TargetWords.ToString(Culture) : string.Empty;

            History.Clear();
            foreach (WritingHistoryEntry entry in await _goals.GetHistoryAsync(ManuscriptId).ConfigureAwait(true))
            {
                History.Add(entry);
            }

            StatusMessage = ProgressSummary;
        }
        finally
        {
            IsBusy = false;
            _loading = false;
            RaiseDerived();
        }
    }

    /// <summary>
    /// Saves the target as it is typed (US3 scenario 1).
    /// </summary>
    /// <remarks>
    /// Half-typed input is not an error worth interrupting anyone for — "5" on the way to "500" is
    /// not a mistake — so an unparseable value simply is not saved, and the status line says so
    /// quietly rather than raising a dialog.
    /// </remarks>
    partial void OnTargetInputChanged(string value)
    {
        // Loading writes this property from the store; that is not the writer typing.
        if (_loading)
        {
            return;
        }

        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, Culture, out int target))
        {
            StatusMessage = string.IsNullOrWhiteSpace(value)
                ? "Enter a number of words to set a daily goal."
                : "Not saved — enter a number of words, for example 500.";
            return;
        }

        StatusMessage = "Saving…";
        _autoSave.Schedule(async () =>
        {
            DomainResult<DailyGoal> result = await _goals.SetGoalAsync(ManuscriptId, target).ConfigureAwait(false);

            if (result.IsFailure)
            {
                StatusMessage = "Not saved — a daily goal must be at least one word.";
                return;
            }

            Progress = await _goals.GetTodayProgressAsync(ManuscriptId).ConfigureAwait(false);
            RaiseDerived();
            StatusMessage = $"Daily goal set to {result.Value.TargetWords:N0} words. {ProgressSummary}";
        });
    }

    /// <summary>Writes any pending target change immediately, for when the screen goes away.</summary>
    [RelayCommand]
    public Task FlushAsync() => _autoSave.FlushAsync();

    /// <summary>
    /// Stops tracking against a target. Confirmed first, not because data is lost — the history is
    /// kept either way — but because a writer on a streak should not clear a goal by mis-tapping.
    /// </summary>
    [RelayCommand]
    public async Task ClearGoalAsync()
    {
        bool confirmed = await _confirmation.ConfirmDestructiveAsync(
            "Clear daily goal?",
            "Progress tracking will stop. Your writing history is kept, and you can set a goal again at any time.",
            "Clear goal").ConfigureAwait(true);

        if (!confirmed)
        {
            StatusMessage = "Your daily goal is unchanged.";
            return;
        }

        DomainResult result = await _goals.ClearGoalAsync(ManuscriptId).ConfigureAwait(true);
        if (result.IsFailure)
        {
            await _errors.ShowAsync("Could not clear the goal", result.Error.Message).ConfigureAwait(true);
            return;
        }

        await LoadAsync().ConfigureAwait(true);
        StatusMessage = "Daily goal cleared. Your writing history is kept.";
    }

    private async void OnAutoSaveFailed(object? sender, Exception error)
        => await _errors.ShowAsync("Your daily goal was not saved", error.Message).ConfigureAwait(true);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _autoSave.Failed -= OnAutoSaveFailed;
        await _autoSave.DisposeAsync().ConfigureAwait(false);
    }

    private static System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.InvariantCulture;

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasActiveGoal));
        OnPropertyChanged(nameof(ProgressSummary));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(HasNoHistory));
    }
}
