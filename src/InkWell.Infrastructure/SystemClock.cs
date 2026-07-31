using InkWell.Application.Abstractions;

namespace InkWell.Infrastructure;

/// <summary>
/// The device clock. "Today" follows the device's local time zone, which is what the writer means
/// by a day (spec.md §Assumptions, FR-012).
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset Now => DateTimeOffset.Now;

    /// <inheritdoc />
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
