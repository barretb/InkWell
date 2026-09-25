using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.Views;

/// <summary>Everything the app has stored, and the controls to remove it (FR-018, SC-008).</summary>
public partial class DataControlsPage : ContentPage
{
    private readonly DataControlsViewModel _viewModel;

    /// <summary>Creates the page.</summary>
    public DataControlsPage(DataControlsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Reloaded every time rather than cached: the counts are the point of the screen, and a
        // stale one would misinform someone deciding what to delete.
        await _viewModel.LoadAsync();
    }
}
