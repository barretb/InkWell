namespace InkWell.Domain.Entities;

/// <summary>
/// A person in the story, scoped to a manuscript (FR-013). Reference material: deleting a
/// character removes only this entry and never affects manuscript content.
/// </summary>
public sealed class Character
{
    /// <summary>Stable identifier, generated on create.</summary>
    public Guid Id { get; set; }

    /// <summary>The manuscript this character belongs to.</summary>
    public Guid ManuscriptId { get; set; }

    /// <summary>The character's name. Validated, trimmed, 1–200 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Freeform continuity notes. May be empty.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>When the character was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the character last changed.</summary>
    public DateTimeOffset ModifiedAt { get; set; }
}
