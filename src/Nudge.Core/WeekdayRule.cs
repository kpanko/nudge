namespace Nudge.Core;

/// <summary>
/// One BYDAY entry. <paramref name="Ordinal"/> is 0 for "every such
/// weekday", 1..5 for the nth such weekday of the month, and -1..-5
/// for the nth from the end of the month.
/// </summary>
public readonly record struct WeekdayRule(
    DayOfWeek Day,
    int Ordinal)
{
    public bool Matches(DateOnly date)
    {
        if (date.DayOfWeek != Day)
        {
            return false;
        }

        if (Ordinal == 0)
        {
            return true;
        }

        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        return Ordinal > 0
            ? ((date.Day - 1) / 7) + 1 == Ordinal
            : ((daysInMonth - date.Day) / 7) + 1 == -Ordinal;
    }
}
