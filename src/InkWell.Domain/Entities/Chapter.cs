namespace InkWell.Domain.Entities;

/// <summary>
/// A unit of the manuscript holding markdown prose (FR-003). Markdown is the storage format, not a
/// rendering of something else, so a chapter round-trips losslessly through the editor.
/// </summary>
public sealed class Chapter
{
    /// <summary>Stable identifier, generated on create.</summary>
    public Guid Id { get; set; }

    /// <summary>The manuscript this chapter belongs to.</summary>
    public Guid ManuscriptId { get; set; }

    /// <summary>The chapter title. Validated, trimmed, 1–200 characters.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The chapter's prose as markdown. May be empty, and may contain inline-image references of
    /// the form <c>![alt](inkwell-img://{imageId})</c>.
    /// </summary>
    public string ContentMarkdown { get; set; } = string.Empty;

    /// <summary>Zero-based position within the manuscript; contiguous across all chapters.</summary>
    public int OrderIndex { get; set; }

    /// <summary>
    /// Cached prose word count. Always recomputed from <see cref="ContentMarkdown"/> by
    /// <see cref="Services.ProseWordCounter"/> on save and never accepted from the editor
    /// (FR-009, SC-005).
    /// </summary>
    public int WordCount { get; set; }

    /// <summary>When the chapter was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the chapter last changed. Bumped on every autosave commit.</summary>
    public DateTimeOffset ModifiedAt { get; set; }
}
