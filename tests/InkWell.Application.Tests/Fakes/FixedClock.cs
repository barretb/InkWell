using InkWell.Application.Abstractions;

namespace InkWell.Application.Tests.Fakes;

/// <summary>
/// A clock the test drives by hand. Day-rollover behaviour (FR-012) is untestable against the real
/// clock without waiting for midnight, so every test that cares about "today" uses this.
/// </summary>
public sealed class FixedClock : IClock
{
    /// <summary>Creates a clock reading <paramref name="now"/>.</summary>
    public FixedClock(DateTimeOffset now) => Now = now;

    /// <summary>Creates a clock reading a fixed, arbitrary instant in the middle of a day.</summary>
    public FixedClock()
        : this(new DateTimeOffset(2026, 3, 14, 10, 30, 0, TimeSpan.Zero))
    {
    }

    /// <inheritdoc />
    public DateTimeOffset Now { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// Read at the offset the clock carries, matching <c>DailyProgressCalculator.LocalDayOf</c>, so
    /// a rollover test behaves the same on a build agent in any time zone.
    /// </remarks>
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => Now = Now.Add(by);

    /// <summary>Moves the clock to one minute past midnight on the next day.</summary>
    public void AdvancePastMidnight()
    {
        DateTime nextDay = Now.DateTime.Date.AddDays(1).AddMinutes(1);
        Now = new DateTimeOffset(nextDay, Now.Offset);
    }

    /// <summary>Moves the clock to a specific instant.</summary>
    public void SetTo(DateTimeOffset now) => Now = now;
}
