namespace InkWell.Domain.Entities;

/// <summary>
/// A novel project — the aggregate root. Deleting a manuscript cascades to every chapter, image,
/// character, plot thread, goal, and writing record it owns (FR-018, SC-008).
/// </summary>
public sealed class Manuscript
{
    /// <summary>Stable identifier, generated on create.</summary>
    public Guid Id { get; set; }

    /// <summary>The writer's title for the novel. Validated, trimmed, 1–200 characters.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>When the manuscript was created. Immutable after create.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the manuscript or any of its children last changed. Drives the library's
    /// newest-modified-first ordering.
    /// </summary>
    public DateTimeOffset ModifiedAt { get; set; }
}
