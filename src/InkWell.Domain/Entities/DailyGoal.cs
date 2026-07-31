namespace InkWell.Domain.Entities;

/// <summary>
/// The writer's daily word-count target for a manuscript (FR-010). At most one exists per
/// manuscript; clearing the goal deactivates it and keeps the writing history intact.
/// </summary>
public sealed class DailyGoal
{
    /// <summary>Stable identifier, generated on create.</summary>
    public Guid Id { get; set; }

    /// <summary>The manuscript this goal belongs to. Unique — one goal per manuscript.</summary>
    public Guid ManuscriptId { get; set; }

    /// <summary>The number of prose words the writer aims to write each day. Positive when active.</summary>
    public int TargetWords { get; set; }

    /// <summary>Whether the goal is currently being tracked.</summary>
    public bool IsActive { get; set; }

    /// <summary>When the goal was first set.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the goal was last changed or cleared.</summary>
    public DateTimeOffset ModifiedAt { get; set; }
}
