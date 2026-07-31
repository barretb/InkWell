using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.Views;

/// <summary>The daily word-count goal and writing history for one manuscript.</summary>
public partial class GoalsPage : ContentPage
{
    private readonly GoalsViewModel _viewModel;

    /// <summary>Creates the page.</summary>
    public GoalsPage(GoalsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Reloaded on every appearance so that returning from a writing session — possibly after
        // midnight — shows the correct day rather than a stale one (FR-012).
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
