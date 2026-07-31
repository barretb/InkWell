namespace InkWell.Application.Abstractions;

/// <summary>
/// Supplies the current time and, critically, the device's local calendar day.
/// </summary>
/// <remarks>
/// Daily goals reset at local midnight and words typed after midnight belong to the new day
/// (FR-012). That rule is only testable if "today" is injectable, so nothing outside an
/// <see cref="IClock"/> implementation may call <c>DateTime.Now</c>.
/// </remarks>
public interface IClock
{
    /// <summary>The current instant, with the device's offset.</summary>
    DateTimeOffset Now { get; }

    /// <summary>The device's current local calendar day.</summary>
    DateOnly Today { get; }
}
