using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Presentation.Services;

namespace InkWell.Presentation.ViewModels;

/// <summary>
/// "Your data": everything InkWell holds, and the controls to remove it (FR-018, SC-008).
/// </summary>
/// <remarks>
/// The screen that makes the app's central promise checkable. It says what is stored, where the
/// file is, and how large it is, and it offers the two deletions the spec requires — one manuscript,
/// or everything. Both are irreversible, so both name exactly what will be lost before they run
/// (FR-005).
/// </remarks>
public sealed partial class DataControlsViewModel : BaseViewModel
{
    private readonly IDataControlsRepository _data;
    private readonly IConfirmationService _confirmation;
    private readonly IErrorPresenter _errors;

    /// <summary>Creates the view model.</summary>
    public DataControlsViewModel(
        IDataControlsRepository data,
        IConfirmationService confirmation,
        IErrorPresenter errors)
    {
        _data = data;
        _confirmation = confirmation;
        _errors = errors;
        Title = "Your data";
    }

    /// <summary>One row per manuscript, with everything it holds counted.</summary>
    public ObservableCollection<ManuscriptDataInventory> Manuscripts { get; } = [];

    /// <summary>Where the encrypted database lives on this device.</summary>
    [ObservableProperty]
    public partial string DatabasePath { get; set; } = string.Empty;

    /// <summary>How large that file currently is.</summary>
    [ObservableProperty]
    public partial long DatabaseByteLength { get; set; }

    /// <summary>True when the app holds nothing at all.</summary>
    public bool IsEmpty => Manuscripts.Count == 0 && !IsBusy;

    /// <summary>
    /// The storage summary in words, so it is readable rather than merely displayed.
    /// </summary>
    public string StorageSummary =>
        $"Everything InkWell has stored is in one encrypted file on this device: {DatabasePath} " +
        $"({DescribeSize(DatabaseByteLength)}). Nothing is sent anywhere unless you export it.";

    /// <summary>Loads the inventory.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            DataInventory inventory = await _data.GetInventoryAsync().ConfigureAwait(true);

            Manuscripts.Clear();
            foreach (ManuscriptDataInventory entry in inventory.Manuscripts)
            {
                Manuscripts.Add(entry);
            }

            DatabasePath = inventory.DatabasePath;
            DatabaseByteLength = inventory.DatabaseByteLength;

            StatusMessage = Manuscripts.Count switch
            {
                0 => "InkWell is not storing any manuscripts.",
                1 => "InkWell is storing 1 manuscript.",
                _ => $"InkWell is storing {Manuscripts.Count} manuscripts.",
            };
        }
        catch (Exception ex) when (StoreFailure.IsExpected(ex))
        {
            (string title, string message) = StoreFailure.Describe(ex);
            await _errors.ShowAsync(title, message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(StorageSummary));
        }
    }

    /// <summary>Deletes one manuscript and everything it owns, after confirmation (FR-005).</summary>
    [RelayCommand]
    public async Task DeleteManuscriptAsync(ManuscriptDataInventory? entry)
    {
        if (entry is null)
        {
            return;
        }

        bool confirmed = await _confirmation.ConfirmDestructiveAsync(
            "Delete this manuscript?",
            $"“{entry.Title}” will be permanently deleted from this device: " +
            $"{Describe(entry.ChapterCount, "chapter")} ({entry.WordCount:N0} words), " +
            $"{Describe(entry.InlineImageCount, "image")}, " +
            $"{Describe(entry.CharacterCount, "character")}, " +
            $"{Describe(entry.PlotThreadCount, "plot thread")}, and " +
            $"{Describe(entry.WritingRecordCount, "day")} of writing history. " +
            "This cannot be undone.",
            "Delete").ConfigureAwait(true);

        if (!confirmed)
        {
            StatusMessage = "Nothing was deleted.";
            return;
        }

        try
        {
            bool deleted = await _data.DeleteManuscriptDataAsync(entry.ManuscriptId).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            StatusMessage = deleted
                ? $"Deleted “{entry.Title}” and everything in it."
                : "That manuscript had already been deleted.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            await _errors.ShowAsync("Could not delete", ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Erases everything, after a confirmation that spells out the whole cost (FR-005, SC-008).
    /// </summary>
    /// <remarks>
    /// The confirmation says the encryption key goes too, because that is what makes this
    /// unrecoverable even from a backup, and someone about to press it deserves to know.
    /// </remarks>
    [RelayCommand]
    public async Task DeleteAllAsync()
    {
        int manuscripts = Manuscripts.Count;
        int words = Manuscripts.Sum(m => m.WordCount);

        bool confirmed = await _confirmation.ConfirmDestructiveAsync(
            "Delete everything?",
            $"All {Describe(manuscripts, "manuscript")} ({words:N0} words), every chapter, image, " +
            "character, plot thread, goal, and day of writing history will be permanently deleted " +
            "from this device, along with the key that encrypts them. Nothing will be recoverable, " +
            "even from a backup. Export anything you want to keep first. This cannot be undone.",
            "Delete everything").ConfigureAwait(true);

        if (!confirmed)
        {
            StatusMessage = "Nothing was deleted.";
            return;
        }

        IsBusy = true;
        try
        {
            await _data.DeleteAllDataAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (StoreFailure.IsExpected(ex))
        {
            (string title, string message) = StoreFailure.Describe(ex);
            await _errors.ShowAsync(title, message).ConfigureAwait(true);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
        StatusMessage = "Everything has been deleted. InkWell is now empty.";
    }

    private static string Describe(int count, string noun)
    {
        // "1 days of writing history" reads as carelessness in a dialog someone is about to trust.
        string plural = noun.EndsWith('y') ? noun[..^1] + "ies" : noun + "s";
        return count == 1 ? $"1 {noun}" : $"{count} {plural}";
    }

    private static string DescribeSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };
}
