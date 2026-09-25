using InkWell.Application.Abstractions.Dtos;

namespace InkWell.Presentation.Controls;

/// <summary>
/// A second <see cref="IEditorHost"/> built from a native MAUI <see cref="Editor"/>, showing the
/// chapter's markdown source instead of rendering it (research.md §1).
/// </summary>
/// <remarks>
/// <para>
/// The live-preview editor is a <c>contenteditable</c> surface inside a WebView, and how VoiceOver
/// and TalkBack narrate one of those is decided by three different browser engines rather than by
/// InkWell. That is the single largest accessibility risk in the plan, and it is not a risk worth
/// carrying alone: this fallback is a plain platform text control, so a screen-reader user gets the
/// narration, review commands, and text-selection gestures their platform already gives them
/// everywhere else, with nothing in between.
/// </para>
/// <para>
/// The trade is stated plainly to the writer: markdown is shown as written — <c>**bold**</c> rather
/// than bold — and images appear as their reference text rather than as pictures. Nothing is lost
/// by switching in either direction, because markdown is the storage format on both sides
/// (research.md §1). The same chapter opens in either host and saves through the same
/// <c>EditorViewModel</c>, so autosave, word counts, and the daily record behave identically
/// (FR-004, FR-009).
/// </para>
/// <para>
/// Every text rule lives in <see cref="AccessibleEditorDocument"/>, where it is tested; this class
/// is the device-bound shell that shows a string and reports edits. Two parts of the contract have
/// no meaning here and are honoured as no-ops rather than faked: <see cref="ImageRequested"/> is
/// never raised, because a native <c>Editor</c> has no paste-image affordance to raise it from —
/// images arrive from the editor page's picker through <see cref="InsertImageAsync"/> — and
/// <see cref="BridgeFailed"/> is never raised because there is no bridge to fail, which is the
/// whole point of the fallback existing.
/// </para>
/// </remarks>
public sealed class AccessibleEditorFallbackView : ContentView, IEditorHost
{
    private readonly AccessibleEditorDocument _document = new();
    private readonly Editor _editor;
    private readonly Label _modeNotice;
    private readonly VerticalStackLayout _layout;

    /// <summary>Creates the fallback editor.</summary>
    public AccessibleEditorFallbackView()
    {
        _modeNotice = new Label
        {
            Text = _document.ModeNotice,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(0, 0, 0, 8),
        };

        // Announced when focus first enters the region, rather than leaving the writer to work out
        // why their formatting looks like punctuation.
        SemanticProperties.SetDescription(_modeNotice, _modeNotice.Text);
        SemanticProperties.SetHeadingLevel(_modeNotice, SemanticHeadingLevel.Level2);

        _editor = new Editor
        {
            AutoSize = EditorAutoSizeOption.Disabled,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
            IsSpellCheckEnabled = true,
            IsTextPredictionEnabled = false,

            // The same guidance the web editor's placeholder carries, so an empty chapter is never
            // a blank screen with nothing to say for itself in either surface (spec.md edge case
            // "empty states").
            Placeholder = AccessibleEditorDocument.EmptyChapterHint,
        };

        SemanticProperties.SetDescription(_editor, "Chapter text, in markdown");
        _editor.TextChanged += OnTextChanged;
        _editor.Unfocused += OnUnfocused;

        _layout = new VerticalStackLayout
        {
            Padding = new Thickness(16),
            Children = { _modeNotice, _editor },
        };

        Content = _layout;
    }

    /// <inheritdoc />
    public event EventHandler<EditorContentChanged>? ContentChanged;

    /// <inheritdoc />
    public event EventHandler? FlushRequested;

    // CS0067 — these two are never raised, and that is the design rather than an oversight. They
    // are part of IEditorHost because the web surface needs them; this surface has neither a
    // paste-image affordance nor a bridge that can fail. Declaring them and leaving them silent is
    // honest. Raising them from somewhere contrived, to quiet the compiler, would not be.
#pragma warning disable CS0067

    /// <summary>
    /// Never raised. A native <see cref="Editor"/> has no paste-or-drop image affordance; the
    /// editor page's picker calls <see cref="InsertImageAsync"/> directly instead.
    /// </summary>
    public event EventHandler<EditorImageRequested>? ImageRequested;

    /// <summary>
    /// Never raised. There is no bridge in this host, which is precisely why it is the fallback
    /// (research.md §5.4).
    /// </summary>
    public event EventHandler<string>? BridgeFailed;

#pragma warning restore CS0067

    /// <inheritdoc />
    public event EventHandler<Guid>? ImageMissingAltText;

    /// <inheritdoc />
    public event EventHandler? DistractionFreeToggleRequested;

    /// <inheritdoc />
    public Task LoadChapterAsync(ChapterContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _document.Load(content);
        _editor.Text = _document.Text;
        _editor.CursorPosition = 0;

        _modeNotice.Text = _document.ModeNotice;
        SemanticProperties.SetDescription(_modeNotice, _document.ModeNotice);
        SemanticProperties.SetDescription(_editor, $"Chapter text for {content.Title}, in markdown");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetDistractionFreeAsync(bool enabled)
    {
        _document.SetDistractionFree(enabled);
        _modeNotice.IsVisible = !enabled;
        _layout.Padding = enabled ? new Thickness(24, 32) : new Thickness(16);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FocusAsync()
    {
        _editor.Focus();
        _editor.CursorPosition = _document.Caret;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InsertImageAsync(InlineImageReference image)
    {
        ArgumentNullException.ThrowIfNull(image);

        _document.SetCaret(_editor.CursorPosition);
        _editor.Text = _document.InsertImage(image);
        _editor.CursorPosition = _document.Caret;

        if (image.IsMissingAltText)
        {
            ImageMissingAltText?.Invoke(this, image.Id);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Asks for focus mode, for the page's visible control to call. There is no in-editor keyboard
    /// route to add here: a native <see cref="Editor"/> does not swallow accelerators the way a
    /// focused WebView does, so the page's own shortcut reaches it normally.
    /// </summary>
    public void RequestDistractionFreeToggle() => DistractionFreeToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _document.SetCaret(_editor.CursorPosition);

        if (_document.TryAcceptEdit(e.NewTextValue, out EditorContentChanged change))
        {
            ContentChanged?.Invoke(this, change);
        }
    }

    private void OnUnfocused(object? sender, FocusEventArgs e) => FlushRequested?.Invoke(this, EventArgs.Empty);
}
