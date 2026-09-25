using InkWell.Presentation;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.Views;

/// <summary>Exporting a manuscript or chapter to EPUB or PDF (FR-018).</summary>
public partial class ExportPage : ContentPage, IQueryAttributable
{
    private readonly ExportViewModel _viewModel;

    /// <summary>Creates the page.</summary>
    public ExportPage(ExportViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.TryGetValue(Routes.ManuscriptIdParameter, out object? manuscript) && manuscript is Guid manuscriptId)
        {
            _viewModel.ManuscriptId = manuscriptId;
        }

        if (query.TryGetValue(Routes.ManuscriptTitleParameter, out object? title) && title is string manuscriptTitle)
        {
            _viewModel.ManuscriptTitle = manuscriptTitle;
        }

        // Arriving from the editor offers the extra "just this chapter" option; arriving from the
        // library does not, and the page hides that section rather than showing a dead control.
        if (query.TryGetValue(Routes.ChapterIdParameter, out object? chapter) && chapter is Guid chapterId)
        {
            _viewModel.ChapterId = chapterId;
        }

        if (query.TryGetValue(Routes.ChapterTitleParameter, out object? chapterTitle) && chapterTitle is string name)
        {
            _viewModel.ChapterTitle = name;
        }
    }
}
