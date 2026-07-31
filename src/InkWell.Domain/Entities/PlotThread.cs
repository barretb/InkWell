namespace InkWell.Domain.Entities;

/// <summary>
/// A narrative thread tracked across the manuscript (FR-014). Same isolation guarantee as
/// <see cref="Character"/>: deleting one never affects manuscript content.
/// </summary>
public sealed class PlotThread
{
    /// <summary>Stable identifier, generated on create.</summary>
    public Guid Id { get; set; }

    /// <summary>The manuscript this thread belongs to.</summary>
    public Guid ManuscriptId { get; set; }

    /// <summary>The thread's title. Validated, trimmed, 1–200 characters.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Freeform notes about the thread. May be empty.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>When the thread was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the thread last changed.</summary>
    public DateTimeOffset ModifiedAt { get; set; }
}
