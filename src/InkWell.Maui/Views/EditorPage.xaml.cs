using InkWell.Presentation.Controls;
using InkWell.Presentation.Services;
using InkWell.Presentation.ViewModels;

namespace InkWell.Maui.Views;

/// <summary>The chapter editor.</summary>
public partial class EditorPage : ContentPage
{
    private readonly EditorViewModel _viewModel;
    private readonly IEditorPreferences _preferences;
    private IEditorHost _host = null!;
    private bool _hasLoaded;

    /// <summary>Creates the page.</summary>
    public EditorPage(EditorViewModel viewModel, IEditorPreferences preferences)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _preferences = preferences;
        BindingContext = viewModel;

        BuildEditorSurface();
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

    /// <summary>
    /// Builds the editing surface the writer has chosen and connects the view model to it.
    /// </summary>
    /// <remarks>
    /// <c>Attach</c> detaches from any previous host first, so this is also the swap path: the view
    /// model, its autosave coordinator, and the open chapter all survive a change of surface,
    /// because the only thing that changes is which control renders the markdown.
    /// </remarks>
    private void BuildEditorSurface()
    {
        _host = _preferences.UseAccessibleEditor
            ? new AccessibleEditorFallbackView()
            : new EditorHostView();

        EditorSlot.Content = (View)_host;
        _viewModel.Attach(_host);

        EditorModeButton.Text = _preferences.UseAccessibleEditor ? "Formatted editor" : "Accessibility mode";
        SemanticProperties.SetHint(
            EditorModeButton,
            _preferences.UseAccessibleEditor
                ? "Switches back to the formatted editor, which shows bold text and images as they will appear."
                : "Switches to a plain text editor that shows markdown as written. Better with a screen reader.");
    }

    /// <summary>
    /// Swaps between the live-preview editor and the native accessibility-mode editor.
    /// </summary>
    /// <remarks>
    /// The pending edit is committed before the swap, because the two surfaces do not share a
    /// document — losing an uncommitted keystroke here would be exactly the failure FR-004 exists
    /// to prevent. The chapter is then reloaded from the store into the new surface, so both sides
    /// start from the same saved markdown.
    /// </remarks>
    private async void OnToggleEditorModeClicked(object? sender, EventArgs e)
    {
        await _viewModel.FlushAsync();

        _preferences.UseAccessibleEditor = !_preferences.UseAccessibleEditor;
        BuildEditorSurface();

        await _viewModel.LoadAsync();
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

        byte[] bytes = buffer.ToArray();
        string mimeType = picked.ContentType ?? "image/png";
        string? description = string.IsNullOrWhiteSpace(altText) ? null : altText;

        // The web host routes the picked file through the same paste/drop path its own editor uses;
        // the native fallback has no such path, so the view model inserts into it directly.
        if (_host is EditorHostView web)
        {
            web.RaiseImageRequested(_viewModel.ChapterId, bytes, mimeType, description);
        }
        else
        {
            await _viewModel.InsertImageAsync(bytes, mimeType, description);
        }
    }
}
