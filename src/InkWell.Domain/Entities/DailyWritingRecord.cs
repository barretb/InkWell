namespace InkWell.Domain.Entities;

/// <summary>
/// Words written on one local calendar day — a single row of the writing history (FR-011, FR-012).
/// One record exists per manuscript per date; days on which nothing was written have no record.
/// </summary>
public sealed class DailyWritingRecord
{
    /// <summary>Stable identifier, generated on create.</summary>
    public Guid Id { get; set; }

    /// <summary>The manuscript these words belong to.</summary>
    public Guid ManuscriptId { get; set; }

    /// <summary>
    /// The local calendar day the words were typed on. Day boundaries follow the device's time
    /// zone, so words typed after midnight belong to the new day (FR-012).
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>Net prose words attributed to this day. Never negative.</summary>
    public int WordsWritten { get; set; }

    /// <summary>
    /// The target that applied on this day, captured when the record was written. Snapshotting it
    /// keeps history honest when the writer later changes or clears the goal.
    /// </summary>
    public int? GoalTarget { get; set; }

    /// <summary>Whether <see cref="WordsWritten"/> reached <see cref="GoalTarget"/> on this day.</summary>
    public bool GoalMet { get; set; }
}
