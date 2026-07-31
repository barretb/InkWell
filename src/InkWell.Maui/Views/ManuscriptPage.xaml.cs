using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.Views;

/// <summary>One manuscript and its chapters.</summary>
public partial class ManuscriptPage : ContentPage
{
    private readonly ManuscriptViewModel _viewModel;

    /// <summary>Creates the page.</summary>
    public ManuscriptPage(ManuscriptViewModel viewModel)
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
}
