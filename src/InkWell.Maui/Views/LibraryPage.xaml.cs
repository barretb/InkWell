using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.Views;

/// <summary>The library of manuscripts — the app's home screen.</summary>
public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel _viewModel;

    /// <summary>Creates the page.</summary>
    public LibraryPage(LibraryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Reloaded on every appearance so returning from a manuscript shows current counts.
        await _viewModel.LoadAsync();
    }
}
