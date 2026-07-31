using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.Views;

/// <summary>Plot threads for one manuscript.</summary>
public partial class PlotThreadsPage : ContentPage
{
    private readonly PlotThreadsViewModel _viewModel;

    /// <summary>Creates the page.</summary>
    public PlotThreadsPage(PlotThreadsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    /// <inheritdoc />
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        // Navigating away mid-edit must not be the thing that loses the change (FR-004).
        await _viewModel.FlushAsync();
    }
}
