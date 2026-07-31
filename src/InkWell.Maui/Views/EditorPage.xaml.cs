using InkWell.Presentation.Controls;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.Views;

/// <summary>The chapter editor.</summary>
public partial class EditorPage : ContentPage
{
    private readonly EditorViewModel _viewModel;
    private bool _hasLoaded;

    /// <summary>Creates the page.</summary>
    public EditorPage(EditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Attach wires every editor event, including the keyboard focus-mode request that the web
        // layer raises (Ctrl/Cmd+Shift+F to enter, Escape to leave). The page deliberately owns
        // none of that logic, so the button and the shortcut cannot drift apart.
        _viewModel.Attach(Editor);
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_hasLoaded)
        {
            // Returning from a reference lookup: the chapter is already open and untouched, so
            // reloading it would be the very thing that loses the writer's place (FR-015).
            await _viewModel.ResumeWritingAsync();
            return;
        }

        _hasLoaded = true;
        await _viewModel.LoadAsync();
    }

    /// <inheritdoc />
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        // Navigating away is one of the flush points that bounds crash loss (SC-003).
        await _viewModel.FlushAsync();
    }

    private async void OnAddImageClicked(object? sender, EventArgs e)
    {
        FileResult? picked = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Choose an image to embed",
            FileTypes = FilePickerFileType.Images,
        });

        if (picked is null)
        {
            return;
        }

        await using Stream stream = await picked.OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        // Alt text is asked for at the moment of insertion, when the writer still remembers what
        // the picture shows. It is optional — an image without it is accepted and then flagged
        // rather than blocked (FR-019 edge case).
        string? altText = await DisplayPromptAsync(
            "Describe this image",
            "What would someone hear if they could not see it? You can leave this blank and add it later.",
            accept: "Add image",
            cancel: "Skip",
            placeholder: "For example: the frozen mill at dusk");

        Editor.RaiseImageRequested(
            _viewModel.ChapterId,
            buffer.ToArray(),
            picked.ContentType ?? "image/png",
            string.IsNullOrWhiteSpace(altText) ? null : altText);
    }
}
