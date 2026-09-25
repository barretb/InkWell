using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InkWell.Application.Abstractions;
using InkWell.Application.Abstractions.Dtos;
using InkWell.Presentation.Services;

namespace InkWell.Presentation.ViewModels;

/// <summary>
/// Exporting a manuscript or a single chapter to EPUB or PDF (FR-018, SC-009).
/// </summary>
/// <remarks>
/// <para>
/// This is the one place in the app where content leaves the device, so the sequence is deliberate:
/// the writer picks the format, the platform's own dialog asks them where, and only then is
/// anything written. Nothing is exported without both of those answers, and no destination is ever
/// chosen on their behalf (FR-017).
/// </para>
/// <para>
/// The work itself runs off the UI thread. A 150,000-word manuscript with embedded images is a real
/// piece of work to render, and it must not freeze the app while it happens (constitution §V).
/// </para>
/// </remarks>
public sealed partial class ExportViewModel : BaseViewModel
{
    private readonly IExportService _export;
    private readonly IFileDestinationPicker _picker;
    private readonly IErrorPresenter _errors;

    /// <summary>Creates the view model.</summary>
    public ExportViewModel(
        IExportService export,
        IFileDestinationPicker picker,
        IErrorPresenter errors)
    {
        _export = export;
        _picker = picker;
        _errors = errors;
        Title = "Export";
    }

    /// <summary>The manuscript being exported.</summary>
    [ObservableProperty]
    public partial Guid ManuscriptId { get; set; }

    /// <summary>Its title, used to suggest a file name.</summary>
    [ObservableProperty]
    public partial string ManuscriptTitle { get; set; } = "Manuscript";

    /// <summary>The chapter being exported, when the writer asked for just one.</summary>
    [ObservableProperty]
    public partial Guid? ChapterId { get; set; }

    /// <summary>That chapter's title.</summary>
    [ObservableProperty]
    public partial string? ChapterTitle { get; set; }

    /// <summary>
    /// Whether the writer arrived from a chapter, so the single-chapter options are worth offering.
    /// </summary>
    public bool HasChapter => ChapterId is not null;

    partial void OnChapterIdChanged(Guid? value) => OnPropertyChanged(nameof(HasChapter));

    /// <summary>The most recent export, so the writer can see where it went.</summary>
    [ObservableProperty]
    public partial ExportResult? LastResult { get; set; }

    /// <summary>Exports the whole manuscript as an EPUB.</summary>
    [RelayCommand]
    public Task ExportManuscriptEpubAsync() => ExportManuscriptAsync(ExportFormat.Epub);

    /// <summary>Exports the whole manuscript as a PDF.</summary>
    [RelayCommand]
    public Task ExportManuscriptPdfAsync() => ExportManuscriptAsync(ExportFormat.Pdf);

    /// <summary>Exports the open chapter as an EPUB.</summary>
    [RelayCommand]
    public Task ExportChapterEpubAsync() => ExportChapterAsync(ExportFormat.Epub);

    /// <summary>Exports the open chapter as a PDF.</summary>
    [RelayCommand]
    public Task ExportChapterPdfAsync() => ExportChapterAsync(ExportFormat.Pdf);

    /// <summary>Exports every chapter to its own EPUB file in a folder the writer chooses.</summary>
    [RelayCommand]
    public Task ExportEachChapterEpubAsync() => ExportEachChapterAsync(ExportFormat.Epub);

    /// <summary>Exports every chapter to its own PDF file in a folder the writer chooses.</summary>
    [RelayCommand]
    public Task ExportEachChapterPdfAsync() => ExportEachChapterAsync(ExportFormat.Pdf);

    /// <summary>Exports the whole manuscript in <paramref name="format"/>.</summary>
    public async Task ExportManuscriptAsync(ExportFormat format)
    {
        string suggested = SuggestName(ManuscriptTitle, format);
        string? destination = await _picker.PickSaveLocationAsync(suggested).ConfigureAwait(true);
        if (destination is null)
        {
            // Cancelling a save dialog is a decision, not a failure.
            StatusMessage = "Export cancelled.";
            return;
        }

        await RunAsync(
            () => _export.ExportManuscriptAsync(ManuscriptId, format, destination),
            $"Exporting “{ManuscriptTitle}”…").ConfigureAwait(true);
    }

    /// <summary>Exports the open chapter in <paramref name="format"/>.</summary>
    public async Task ExportChapterAsync(ExportFormat format)
    {
        if (ChapterId is not { } chapterId)
        {
            return;
        }

        string suggested = SuggestName(ChapterTitle ?? "Chapter", format);
        string? destination = await _picker.PickSaveLocationAsync(suggested).ConfigureAwait(true);
        if (destination is null)
        {
            StatusMessage = "Export cancelled.";
            return;
        }

        await RunAsync(
            () => _export.ExportChapterAsync(chapterId, format, destination),
            $"Exporting “{ChapterTitle}”…").ConfigureAwait(true);
    }

    /// <summary>Exports every chapter to its own file in a folder the writer chooses.</summary>
    public async Task ExportEachChapterAsync(ExportFormat format)
    {
        string? folder = await _picker.PickFolderAsync().ConfigureAwait(true);
        if (folder is null)
        {
            StatusMessage = "Export cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Exporting each chapter of “{ManuscriptTitle}”…";
        try
        {
            IReadOnlyList<ExportResult> results = await Task
                .Run(() => _export.ExportManuscriptAllChaptersAsync(ManuscriptId, format, folder))
                .ConfigureAwait(true);

            LastResult = results.Count > 0 ? results[^1] : null;
            int images = results.Sum(r => r.EmbeddedImageCount);

            StatusMessage = results.Count == 0
                ? "There were no chapters to export."
                : $"Exported {Describe(results.Count, "chapter")} to {folder}" + DescribeImages(images) + ".";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ReportAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunAsync(Func<Task<ExportResult>> export, string busyMessage)
    {
        IsBusy = true;
        StatusMessage = busyMessage;
        try
        {
            // Off the UI thread: rendering a long manuscript with images is real work, and the app
            // must stay responsive while it happens (constitution §V).
            ExportResult result = await Task.Run(export).ConfigureAwait(true);

            LastResult = result;
            StatusMessage = $"Saved to {result.FilePath}"
                + DescribeImages(result.EmbeddedImageCount)
                + $" ({DescribeSize(result.ByteLength)}).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ReportAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReportAsync(Exception ex)
    {
        // The manuscript is untouched by a failed export, and saying so is the useful part: the
        // writer needs to know their work is safe, not just that something went wrong.
        StatusMessage = "Export failed. Your manuscript is unchanged.";

        (string title, string message) = StoreFailure.Describe(ex);
        await _errors.ShowAsync(title, message).ConfigureAwait(true);
    }

    /// <summary>
    /// The name offered in the save dialog.
    /// </summary>
    /// <remarks>
    /// Shared with the exporter's batch naming through <see cref="ExportFileName"/>, so the name
    /// InkWell suggests and the name it writes in a per-chapter export cannot drift apart. It is
    /// only ever a suggestion: the writer types over it, and the path they confirm is used verbatim.
    /// </remarks>
    private static string SuggestName(string title, ExportFormat format)
        => ExportFileName.For(title, format);

    private static string DescribeImages(int count) => count switch
    {
        0 => string.Empty,
        1 => ", with 1 image embedded",
        _ => $", with {count} images embedded",
    };

    private static string DescribeSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };

    private static string Describe(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
