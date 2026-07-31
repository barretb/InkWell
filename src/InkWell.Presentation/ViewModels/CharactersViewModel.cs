using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InkWell.Application.UseCases;
using InkWell.Domain.Abstractions;
using InkWell.Domain.Entities;
using InkWell.Presentation.Services;

namespace InkWell.Presentation.ViewModels;

/// <summary>
/// Character profiles for one manuscript (FR-013).
/// </summary>
/// <remarks>
/// Edits save themselves. There is no save button here for the same reason there is none in the
/// editor: FR-004 says the writer never performs an explicit save, and a notes field that needed
/// one would be the single place in the app where forgetting costs you your work.
/// </remarks>
public sealed partial class CharactersViewModel : BaseViewModel, IQueryAttributable, IAsyncDisposable
{
    private readonly ReferenceUseCases _references;
    private readonly IConfirmationService _confirmation;
    private readonly IErrorPresenter _errors;
    private readonly Debouncer _autoSave;

    /// <summary>Creates the view model.</summary>
    public CharactersViewModel(
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
        Title = "Characters";
    }

    /// <summary>The manuscript these characters belong to.</summary>
    [ObservableProperty]
    public partial Guid ManuscriptId { get; set; }

    /// <summary>The characters, name-sorted, each editing itself in place.</summary>
    public ObservableCollection<CharacterEditor> Characters { get; } = [];

    /// <summary>The name typed into the new-character field.</summary>
    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    /// <summary>The notes typed into the new-character field.</summary>
    [ObservableProperty]
    public partial string NewNotes { get; set; } = string.Empty;

    /// <summary>True when there are no characters yet.</summary>
    public bool IsEmpty => Characters.Count == 0 && !IsBusy;

    /// <inheritdoc />
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TryGetValue(Routes.ManuscriptIdParameter, out object? value) && value is Guid id)
        {
            ManuscriptId = id;
        }
    }

    /// <summary>Loads the manuscript's characters.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            foreach (CharacterEditor existing in Characters)
            {
                existing.Edited -= OnCharacterEdited;
            }

            Characters.Clear();
            foreach (Character character in await _references.ListCharactersAsync(ManuscriptId).ConfigureAwait(true))
            {
                var editor = new CharacterEditor(character);
                editor.Edited += OnCharacterEdited;
                Characters.Add(editor);
            }

            StatusMessage = Characters.Count switch
            {
                0 => "No characters yet.",
                1 => "1 character.",
                _ => $"{Characters.Count} characters.",
            };
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Adds a character (US4 scenario 1). Creating is a deliberate act, so it stays a command; what
    /// happens to the character afterwards saves itself.
    /// </summary>
    [RelayCommand]
    public async Task CreateAsync()
    {
        DomainResult<Character> created = await _references
            .CreateCharacterAsync(ManuscriptId, NewName, NewNotes)
            .ConfigureAwait(true);

        if (created.IsFailure)
        {
            await _errors.ShowAsync("That character will not save", created.Error.Message).ConfigureAwait(true);
            return;
        }

        NewName = string.Empty;
        NewNotes = string.Empty;
        await LoadAsync().ConfigureAwait(true);
        StatusMessage = $"Added {created.Value.Name}.";
    }

    /// <summary>
    /// Writes any pending edit immediately. Called when the screen goes away, so navigating off
    /// mid-word cannot lose the word.
    /// </summary>
    [RelayCommand]
    public Task FlushAsync() => _autoSave.FlushAsync();

    /// <summary>Deletes a character after the writer confirms (FR-005).</summary>
    [RelayCommand]
    public async Task DeleteAsync(CharacterEditor? character)
    {
        if (character is null)
        {
            return;
        }

        bool confirmed = await _confirmation.ConfirmDestructiveAsync(
            "Delete character?",
            $"{character.Name} and their notes will be permanently deleted. Your chapters are not " +
            "changed — any mention of them in your prose stays exactly as you wrote it.",
            "Delete").ConfigureAwait(true);

        if (!confirmed)
        {
            StatusMessage = "Nothing was deleted.";
            return;
        }

        DomainResult result = await _references.DeleteCharacterAsync(character.Id).ConfigureAwait(true);
        if (result.IsFailure)
        {
            await _errors.ShowAsync("Could not delete", result.Error.Message).ConfigureAwait(true);
            return;
        }

        string name = character.Name;
        await LoadAsync().ConfigureAwait(true);
        StatusMessage = $"Deleted {name}.";
    }

    private void OnCharacterEdited(object? sender, EventArgs e)
    {
        if (sender is not CharacterEditor editor)
        {
            return;
        }

        StatusMessage = "Saving…";
        _autoSave.Schedule(async () =>
        {
            DomainResult result = await _references
                .UpdateCharacterAsync(editor.Id, editor.Name, editor.Notes)
                .ConfigureAwait(false);

            // An empty name is not an error to shout about mid-typing — the writer is probably
            // between characters of a word. It simply is not saved until it is valid again.
            StatusMessage = result.IsSuccess
                ? "Saved"
                : result.Error.Code == DomainErrorCode.ValidationError
                    ? "Not saved — a name is required"
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
