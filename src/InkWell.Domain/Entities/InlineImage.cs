namespace InkWell.Domain.Entities;

/// <summary>
/// An image embedded in a chapter. The bytes are copied into the encrypted store on insert so the
/// manuscript stays self-contained even if the source file is moved or deleted (FR-003a).
/// </summary>
/// <remarks>
/// Images live in their own table rather than inline in <see cref="Chapter.ContentMarkdown"/> so
/// that frequent autosaves never rewrite image pages (research.md §2).
/// </remarks>
public sealed class InlineImage
{
    /// <summary>
    /// Stable identifier. Referenced from markdown as <c>inkwell-img://{Id}</c> and resolved to a
    /// data URI when the chapter is loaded into the editor.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>The chapter this image belongs to. Cascade-deletes with the chapter.</summary>
    public Guid ChapterId { get; set; }

    /// <summary>The embedded image bytes.</summary>
    public byte[] Bytes { get; set; } = [];

    /// <summary>The image's MIME type, for example <c>image/png</c>.</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// Alternative text for screen-reader users. Optional: writers may insert an image without it,
    /// but its absence is surfaced as an accessibility gap rather than silently accepted (FR-019).
    /// </summary>
    public string? AltText { get; set; }

    /// <summary>Denormalised byte length, so export can size a manifest without loading bytes.</summary>
    public int ByteLength { get; set; }

    /// <summary>When the image was embedded.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Whether this image still needs alternative text (FR-019 accessibility gap).</summary>
    public bool IsMissingAltText => string.IsNullOrWhiteSpace(AltText);
}
