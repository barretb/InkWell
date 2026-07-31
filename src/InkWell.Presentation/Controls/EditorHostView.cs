using System.Text.Json;
using System.Text.Json.Serialization;
using InkWell.Application.Abstractions.Dtos;

namespace InkWell.Presentation.Controls;

/// <summary>
/// Hosts the CodeMirror 6 editor in a <see cref="HybridWebView"/> and implements the JS↔C# bridge
/// (contracts/chapter-editor-bridge.md).
/// </summary>
/// <remarks>
/// <para>
/// Messages cross as JSON over <c>SendRawMessage</c> / <c>RawMessageReceived</c> rather than through
/// typed interop, for two reasons: the payloads are small and uniform, and a single envelope shape
/// keeps the JS side free of generated glue that would have to be rebuilt whenever a C# signature
/// moves.
/// </para>
/// <para>
/// Image bytes are the one large payload, and they only ever cross once per insert — never on the
/// keystroke path, where only the markdown delta travels (research.md §1).
/// </para>
/// </remarks>
public sealed class EditorHostView : ContentView, IEditorHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// How long to wait for the web editor to announce itself before declaring the bridge broken.
    /// Generous: the page is local, so anything approaching this means something is actually wrong.
    /// </summary>
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);

    private readonly HybridWebView _web;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Guid _chapterId;
    private ChapterContent? _openChapter;

    /// <summary>Creates the editor host.</summary>
    public EditorHostView()
    {
        _web = new HybridWebView
        {
            HybridRoot = "wwwroot",
            DefaultFile = "index.html",
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
        };

        _web.RawMessageReceived += OnRawMessageReceived;
        Content = _web;
    }

    /// <inheritdoc />
    public event EventHandler<EditorContentChanged>? ContentChanged;

    /// <inheritdoc />
    public event EventHandler? FlushRequested;

    /// <inheritdoc />
    public event EventHandler<EditorImageRequested>? ImageRequested;

    /// <inheritdoc />
    public event EventHandler<Guid>? ImageMissingAltText;

    /// <summary>
    /// Raised when the writer asks for focus mode from inside the editor, by keyboard. The web
    /// layer owns those keys because a focused WebView consumes them before any native accelerator
    /// sees them (FR-008).
    /// </summary>
    public event EventHandler? DistractionFreeToggleRequested;

    /// <inheritdoc />
    public event EventHandler<string>? BridgeFailed;

    /// <summary>
    /// Feeds an image chosen through the native file picker into the same path the editor's own
    /// paste and drop use, so there is one insertion route rather than two.
    /// </summary>
    public void RaiseImageRequested(Guid chapterId, byte[] bytes, string mimeType, string? altText)
        => ImageRequested?.Invoke(this, new EditorImageRequested(chapterId, bytes, mimeType, altText));

    /// <inheritdoc />
    public Task LoadChapterAsync(ChapterContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _chapterId = content.Id;

        // Remembered so the chapter can be restored if the WebView reloads — which Android does
        // freely when the app is backgrounded. Without this the writer would come back to an empty
        // editor whose keystrokes go nowhere.
        _openChapter = content;

        return SendAsync("loadChapter", new
        {
            chapterId = content.Id,
            markdown = content.ContentMarkdown,
            images = content.Images.Select(i => new
            {
                id = i.Id,
                dataUri = i.DataUri,
                altText = i.AltText,
            }),
        });
    }

    /// <inheritdoc />
    public Task SetDistractionFreeAsync(bool enabled) => SendAsync("setDistractionFree", new { enabled });

    /// <inheritdoc />
    public Task FocusAsync() => SendAsync("focusEditor", new { });

    /// <inheritdoc />
    public Task InsertImageAsync(InlineImageReference image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return SendAsync("insertImage", new
        {
            id = image.Id,
            dataUri = image.DataUri,
            altText = image.AltText,
        });
    }

    private async Task SendAsync(string type, object payload)
    {
        string json = JsonSerializer.Serialize(new { type, payload }, JsonOptions);

        // Nothing may be sent before the web editor exists to receive it. `SendRawMessage` into a
        // page that has not finished loading is silently discarded, and the first message is
        // `loadChapter` — the one that tells the editor which chapter it is editing. Losing it
        // leaves the editor with no chapter id, which makes it suppress every `contentChanged`,
        // which means nothing the writer types is ever saved. Hence the handshake.
        if (!await WaitForEditorAsync().ConfigureAwait(false))
        {
            return;
        }

        // Marshalled explicitly: bridge sends can originate from an autosave continuation, which is
        // not on the UI thread, and every WebView engine requires its own thread here.
        await MainThread.InvokeOnMainThreadAsync(() => _web.SendRawMessage(json)).ConfigureAwait(false);
    }

    private async Task<bool> WaitForEditorAsync()
    {
        if (_ready.Task.IsCompletedSuccessfully)
        {
            return true;
        }

        try
        {
            await _ready.Task.WaitAsync(ReadyTimeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            BridgeFailed?.Invoke(
                this,
                "The writing surface did not finish loading, so your typing is not being saved. " +
                "Close and reopen the chapter. If it keeps happening, copy your text somewhere safe.");
            return false;
        }
    }

    /// <summary>
    /// Handles the editor announcing that it is loaded and listening.
    /// </summary>
    /// <remarks>
    /// A second announcement means the WebView reloaded underneath us and the editor is now empty,
    /// so the open chapter is pushed back into it.
    /// </remarks>
    private void OnEditorReady()
    {
        bool reloaded = _ready.Task.IsCompleted;
        _ready.TrySetResult();

        if (reloaded && _openChapter is { } chapter)
        {
            _ = LoadChapterAsync(chapter);
        }
    }

    private void OnRawMessageReceived(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Message))
        {
            return;
        }

        BridgeMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<BridgeMessage>(e.Message, JsonOptions);
        }
        catch (JsonException)
        {
            // A malformed message from the web layer must never take the app down mid-sentence.
            return;
        }

        if (message is null)
        {
            return;
        }

        switch (message.Type)
        {
            case "contentChanged":
                if (message.Markdown is not null)
                {
                    ContentChanged?.Invoke(this, new EditorContentChanged(ResolveChapter(message), message.Markdown));
                }

                break;

            case "flushNow":
                if (message.Markdown is not null)
                {
                    ContentChanged?.Invoke(this, new EditorContentChanged(ResolveChapter(message), message.Markdown));
                }

                FlushRequested?.Invoke(this, EventArgs.Empty);
                break;

            case "insertImageRequested":
                if (message.Bytes is not null && message.MimeType is not null)
                {
                    ImageRequested?.Invoke(this, new EditorImageRequested(
                        ResolveChapter(message),
                        Convert.FromBase64String(message.Bytes),
                        message.MimeType,
                        message.AltText));
                }

                break;

            case "imageMissingAltText":
                if (message.ImageId is { } imageId)
                {
                    ImageMissingAltText?.Invoke(this, imageId);
                }

                break;

            case "toggleDistractionFree":
                DistractionFreeToggleRequested?.Invoke(this, EventArgs.Empty);
                break;

            case "editorReady":
                OnEditorReady();
                break;

            default:
                break;
        }
    }

    private Guid ResolveChapter(BridgeMessage message) => message.ChapterId ?? _chapterId;

    private sealed record BridgeMessage(
        string Type,
        Guid? ChapterId,
        string? Markdown,
        string? Bytes,
        string? MimeType,
        string? AltText,
        Guid? ImageId);
}
