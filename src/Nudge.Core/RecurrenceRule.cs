using System.Globalization;

namespace Nudge.Core;

/// <summary>
/// A deliberately small subset of the RFC 5545 recurrence rule,
/// working on whole days. Supported parts: FREQ (DAILY, WEEKLY,
/// MONTHLY, YEARLY), INTERVAL, BYDAY, BYMONTHDAY, BYMONTH, UNTIL and
/// WKST. Anything else is rejected by <see cref="Parse"/> rather than
/// silently ignored.
/// </summary>
/// <remarks>
/// One intentional difference from RFC 5545: a day of the month that
/// does not exist in a given month is moved back to the last day of
/// that month instead of being skipped. "Monthly on the 31st" falls
/// on 30 April and on 28 or 29 February.
/// </remarks>
public sealed class RecurrenceRule
{
    /// <summary>400 years: one full Gregorian calendar cycle.</summary>
    private const int SearchLimitDays = 146_097;

    private static readonly string[] DayCodes =
        ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    private RecurrenceRule(
        Frequency frequency,
        int interval,
        IReadOnlyList<WeekdayRule> byDay,
        IReadOnlyList<int> byMonthDay,
        IReadOnlyList<int> byMonth,
        DateOnly? until,
        DayOfWeek weekStart)
    {
        Frequency = frequency;
        Interval = interval;
        ByDay = byDay;
        ByMonthDay = byMonthDay;
        ByMonth = byMonth;
        Until = until;
        WeekStart = weekStart;
    }

    public Frequency Frequency { get; }

    public int Interval { get; }

    public IReadOnlyList<WeekdayRule> ByDay { get; }

    public IReadOnlyList<int> ByMonthDay { get; }

    public IReadOnlyList<int> ByMonth { get; }

    /// <summary>Last day an occurrence may fall on, inclusive.</summary>
    public DateOnly? Until { get; }

    public DayOfWeek WeekStart { get; }

    /// <summary>Parses an RRULE value such as
    /// <c>FREQ=WEEKLY;INTERVAL=2;BYDAY=FR</c>.</summary>
    /// <exception cref="FormatException">The text is malformed or
    /// uses a part this subset does not support.</exception>
    public static RecurrenceRule Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var body = text.Trim();
        if (body.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
        {
            body = body["RRULE:".Length..];
        }

        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in body.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0 || equals == part.Length - 1)
            {
                throw new FormatException(
                    $"Recurrence part '{part}' is not NAME=VALUE.");
            }

            var name = part[..equals].Trim().ToUpperInvariant();
            var value = part[(equals + 1)..].Trim().ToUpperInvariant();
            if (!parts.TryAdd(name, value))
            {
                throw new FormatException(
                    $"Recurrence part {name} appears more than once.");
            }
        }

        Frequency? frequency = null;
        var interval = 1;
        var byDay = new List<WeekdayRule>();
        var byMonthDay = new List<int>();
        var byMonth = new List<int>();
        DateOnly? until = null;
        var weekStart = DayOfWeek.Monday;

        foreach (var (name, value) in parts)
        {
            switch (name)
            {
                case "FREQ":
                    frequency = ParseFrequency(value);
                    break;
                case "INTERVAL":
                    interval = ParseInt(name, value, 1, int.MaxValue);
                    break;
                case "BYDAY":
                    byDay.AddRange(value.Split(',').Select(ParseWeekday));
                    break;
                case "BYMONTHDAY":
                    byMonthDay.AddRange(
                        value.Split(',').Select(ParseMonthDay));
                    break;
                case "BYMONTH":
                    byMonth.AddRange(
                        value.Split(',')
                            .Select(v => ParseInt(name, v, 1, 12)));
                    break;
                case "UNTIL":
                    until = ParseUntil(value);
                    break;
                case "WKST":
                    weekStart = ParseDayCode(value);
                    break;
                default:
                    throw new FormatException(
                        $"Recurrence part {name} is not supported.");
            }
        }

        if (frequency is not { } freq)
        {
            throw new FormatException("Recurrence rule has no FREQ.");
        }

        // An interval longer than the search window would make an
        // endless series look as if it had ended.
        var maxInterval = freq switch
        {
            Frequency.Daily => SearchLimitDays,
            Frequency.Weekly => SearchLimitDays / 7,
            Frequency.Monthly => 400 * 12,
            _ => 400,
        };
        if (interval > maxInterval)
        {
            throw new FormatException(
                $"INTERVAL={interval} is longer than 400 years.");
        }

        var monthBased =
            freq is Frequency.Monthly or Frequency.Yearly;
        if (!monthBased && byDay.Any(d => d.Ordinal != 0))
        {
            throw new FormatException(
                "BYDAY with a number needs FREQ=MONTHLY or YEARLY.");
        }

        if (freq == Frequency.Weekly && byMonthDay.Count > 0)
        {
            throw new FormatException(
                "BYMONTHDAY cannot be used with FREQ=WEEKLY.");
        }

        if (freq == Frequency.Yearly
            && byMonth.Count == 0
            && (byDay.Count > 0 || byMonthDay.Count > 0))
        {
            throw new FormatException(
                "FREQ=YEARLY with BYDAY or BYMONTHDAY needs BYMONTH.");
        }

        return new RecurrenceRule(
            freq,
            interval,
            byDay.Distinct().ToArray(),
            byMonthDay.Distinct().Order().ToArray(),
            byMonth.Distinct().Order().ToArray(),
            until,
            weekStart);
    }

    /// <summary>
    /// Whether <paramref name="date"/> belongs to the series that
    /// starts at <paramref name="anchor"/>. Parts the rule leaves out
    /// are taken from the anchor: its weekday for WEEKLY, its day of
    /// the month for MONTHLY, its month and day for YEARLY.
    /// </summary>
    public bool IsOccurrence(
        DateOnly anchor,
        DateOnly date)
    {
        if (date < anchor || date > Until)
        {
            return false;
        }

        return PeriodsBetween(anchor, date) % Interval == 0
            && MatchesFilters(anchor, date);
    }

    /// <summary>First occurrence strictly after
    /// <paramref name="after"/>, or null if the series has ended or
    /// has no occurrence in the next 400 years.</summary>
    public DateOnly? NextAfter(
        DateOnly anchor,
        DateOnly after)
    {
        if (after == DateOnly.MaxValue)
        {
            return null;
        }

        return NextOnOrAfter(anchor, after.AddDays(1));
    }

    /// <summary>First occurrence on or after
    /// <paramref name="from"/>, or null if the series has ended or
    /// has no occurrence in the next 400 years.</summary>
    public DateOnly? NextOnOrAfter(
        DateOnly anchor,
        DateOnly from)
    {
        var start = from < anchor ? anchor : from;
        return FirstMatch(start, date => IsOccurrence(anchor, date));
    }

    /// <summary>
    /// First day that is at least one interval after
    /// <paramref name="completedOn"/> and satisfies the BY parts.
    /// With no BY parts this is exactly one interval later. Only
    /// the BY parts written in the rule apply; nothing is taken
    /// from a start date.
    /// </summary>
    public DateOnly? NextCountedFrom(DateOnly completedOn)
    {
        DateOnly earliest;
        try
        {
            earliest = Frequency switch
            {
                Frequency.Daily => completedOn.AddDays(Interval),
                Frequency.Weekly => completedOn.AddDays(
                    checked(7 * Interval)),
                Frequency.Monthly => completedOn.AddMonths(Interval),
                _ => completedOn.AddYears(Interval),
            };
        }
        catch (Exception e) when (
            e is ArgumentOutOfRangeException or OverflowException)
        {
            // Past the end of the calendar: no next occurrence.
            return null;
        }

        return FirstMatch(
            earliest,
            date => date <= (Until ?? DateOnly.MaxValue)
                && MatchesExplicitFilters(date));
    }

    /// <summary>The rule in canonical RRULE form.</summary>
    public override string ToString()
    {
        var parts = new List<string>
        {
            "FREQ=" + Frequency.ToString().ToUpperInvariant(),
        };

        if (Interval != 1)
        {
            parts.Add(
                "INTERVAL=" + Interval.ToString(
                    CultureInfo.InvariantCulture));
        }

        if (ByMonth.Count > 0)
        {
            parts.Add("BYMONTH=" + string.Join(',', ByMonth));
        }

        if (ByMonthDay.Count > 0)
        {
            parts.Add("BYMONTHDAY=" + string.Join(',', ByMonthDay));
        }

        if (ByDay.Count > 0)
        {
            parts.Add(
                "BYDAY=" + string.Join(
                    ',',
                    ByDay.Select(FormatWeekday)));
        }

        if (Until is { } until)
        {
            parts.Add(
                "UNTIL=" + until.ToString(
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture));
        }

        if (WeekStart != DayOfWeek.Monday)
        {
            parts.Add("WKST=" + DayCodes[(int)WeekStart]);
        }

        return string.Join(';', parts);
    }

    private DateOnly? FirstMatch(
        DateOnly start,
        Func<DateOnly, bool> matches)
    {
        var last = Until ?? DateOnly.MaxValue;
        var remaining = DateOnly.MaxValue.DayNumber - start.DayNumber;
        if (remaining > SearchLimitDays)
        {
            var limit = start.AddDays(SearchLimitDays);
            last = limit < last ? limit : last;
        }

        for (var day = start.DayNumber; day <= last.DayNumber; day++)
        {
            var date = DateOnly.FromDayNumber(day);
            if (matches(date))
            {
                return date;
            }
        }

        return null;
    }

    private int PeriodsBetween(
        DateOnly anchor,
        DateOnly date)
    {
        return Frequency switch
        {
            Frequency.Daily => date.DayNumber - anchor.DayNumber,
            Frequency.Weekly =>
                (WeekNumber(date) - WeekNumber(anchor)),
            Frequency.Monthly =>
                ((date.Year - anchor.Year) * 12)
                + date.Month - anchor.Month,
            _ => date.Year - anchor.Year,
        };
    }

    /// <summary>Counts weeks since day zero, where a week begins on
    /// <see cref="WeekStart"/>.</summary>
    private int WeekNumber(DateOnly date)
    {
        // Day number 0 (0001-01-01) is a Monday.
        var shift = ((int)WeekStart + 6) % 7;
        return (date.DayNumber - shift + 7) / 7;
    }

    private bool MatchesFilters(
        DateOnly anchor,
        DateOnly date)
    {
        return MatchesMonth(anchor, date)
            && MatchesMonthDay(anchor, date)
            && MatchesWeekday(anchor, date);
    }

    private bool MatchesExplicitFilters(DateOnly date)
    {
        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        return (ByMonth.Count == 0 || ByMonth.Contains(date.Month))
            && (ByMonthDay.Count == 0
                || ByMonthDay.Any(
                    d => date.Day == ClampDay(d, daysInMonth)))
            && (ByDay.Count == 0 || ByDay.Any(d => d.Matches(date)));
    }

    private bool MatchesMonth(
        DateOnly anchor,
        DateOnly date)
    {
        if (ByMonth.Count > 0)
        {
            return ByMonth.Contains(date.Month);
        }

        return Frequency != Frequency.Yearly
            || date.Month == anchor.Month;
    }

    private bool MatchesMonthDay(
        DateOnly anchor,
        DateOnly date)
    {
        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        if (ByMonthDay.Count > 0)
        {
            return ByMonthDay.Any(
                d => date.Day == ClampDay(d, daysInMonth));
        }

        var monthBased =
            Frequency is Frequency.Monthly or Frequency.Yearly;
        return !monthBased
            || ByDay.Count > 0
            || date.Day == ClampDay(anchor.Day, daysInMonth);
    }

    private bool MatchesWeekday(
        DateOnly anchor,
        DateOnly date)
    {
        if (ByDay.Count > 0)
        {
            return ByDay.Any(d => d.Matches(date));
        }

        return Frequency != Frequency.Weekly
            || date.DayOfWeek == anchor.DayOfWeek;
    }

    /// <summary>Turns a BYMONTHDAY value (negative counts from the
    /// end) into a real day of a month with
    /// <paramref name="daysInMonth"/> days.</summary>
    private static int ClampDay(
        int monthDay,
        int daysInMonth)
    {
        var day = monthDay > 0 ? monthDay : daysInMonth + monthDay + 1;
        return Math.Clamp(day, 1, daysInMonth);
    }

    private static Frequency ParseFrequency(string value)
    {
        return value switch
        {
            "DAILY" => Frequency.Daily,
            "WEEKLY" => Frequency.Weekly,
            "MONTHLY" => Frequency.Monthly,
            "YEARLY" => Frequency.Yearly,
            _ => throw new FormatException(
                $"FREQ={value} is not supported."),
        };
    }

    private static int ParseInt(
        string name,
        string value,
        int min,
        int max)
    {
        if (!int.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var number)
            || number < min
            || number > max)
        {
            throw new FormatException(
                $"{name}={value} is not a valid value.");
        }

        return number;
    }

    private static int ParseMonthDay(string value)
    {
        var day = ParseInt("BYMONTHDAY", value, -31, 31);
        if (day == 0)
        {
            throw new FormatException("BYMONTHDAY=0 is not valid.");
        }

        return day;
    }

    private static WeekdayRule ParseWeekday(string value)
    {
        if (value.Length < 2)
        {
            throw new FormatException(
                $"BYDAY={value} is not a valid value.");
        }

        var day = ParseDayCode(value[^2..]);
        var prefix = value[..^2];
        if (prefix.Length == 0)
        {
            return new WeekdayRule(day, 0);
        }

        var ordinal = ParseInt("BYDAY", prefix, -5, 5);
        if (ordinal == 0)
        {
            throw new FormatException(
                $"BYDAY={value} is not a valid value.");
        }

        return new WeekdayRule(day, ordinal);
    }

    private static DayOfWeek ParseDayCode(string code)
    {
        var index = Array.IndexOf(DayCodes, code);
        if (index < 0)
        {
            throw new FormatException(
                $"'{code}' is not a weekday code.");
        }

        return (DayOfWeek)index;
    }

    private static DateOnly ParseUntil(string value)
    {
        // Either a date (20270131) or a date-time (20270131T235959Z);
        // the whole value must be valid but only the date is used.
        string[] formats =
        [
            "yyyyMMdd",
            "yyyyMMdd'T'HHmmss",
            "yyyyMMdd'T'HHmmss'Z'",
        ];
        if (!DateTime.TryParseExact(
                value,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var until))
        {
            throw new FormatException(
                $"UNTIL={value} is not a valid value.");
        }

        return DateOnly.FromDateTime(until);
    }

    private static string FormatWeekday(WeekdayRule rule)
    {
        var code = DayCodes[(int)rule.Day];
        return rule.Ordinal == 0
            ? code
            : rule.Ordinal.ToString(CultureInfo.InvariantCulture) + code;
    }
}
