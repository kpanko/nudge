namespace Nudge.Core;

/// <summary>
/// Turns a wall-clock date and time into an exact instant. Recurrence
/// works on whole days, so daylight-saving changes only matter here.
/// </summary>
public static class LocalTimeResolver
{
    /// <summary>
    /// The instant at which clocks in <paramref name="zone"/> show
    /// the given date and time. A time skipped by a clock change
    /// moves forward by the size of the gap (02:30 becomes 03:30); a
    /// time that happens twice resolves to the first of the two.
    /// </summary>
    public static DateTimeOffset ToInstant(
        DateOnly date,
        TimeOnly time,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        TimeSpan offset;
        if (zone.IsInvalidTime(local))
        {
            // Use the offset in force before the clocks jumped.
            offset = zone.GetUtcOffset(local.AddDays(-1));
        }
        else if (zone.IsAmbiguousTime(local))
        {
            offset = zone.GetAmbiguousTimeOffsets(local).Max();
        }
        else
        {
            offset = zone.GetUtcOffset(local);
        }

        var utc = new DateTimeOffset(local, TimeSpan.Zero) - offset;
        return TimeZoneInfo.ConvertTime(utc, zone);
    }
}
