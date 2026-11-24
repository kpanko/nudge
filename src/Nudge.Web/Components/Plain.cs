using System.Globalization;
using Nudge.Core;

namespace Nudge.Web.Components;

/// <summary>Dates, repeat rules and reminders in words, for reading
/// only. Form values never come from here: they use
/// <see cref="Words"/>.</summary>
public static class Plain
{
    private static readonly DayOfWeek[] WorkWeek =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
    ];

    /// <summary>Today, Tomorrow, Yesterday, or a date such as
    /// Fri 27 Nov, with the year when it is not the owner's.
    /// </summary>
    public static string Day(
        DateOnly date,
        DateOnly today)
    {
        var days = date.DayNumber - today.DayNumber;
        return days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            _ => Absolute(date, today),
        };
    }

    /// <summary>A date such as Fri 27 Nov, never Today; with the year
    /// when it is not the owner's.</summary>
    public static string Absolute(
        DateOnly date,
        DateOnly today)
    {
        var format = date.Year == today.Year
            ? "ddd d MMM"
            : "ddd d MMM yyyy";
        return date.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>A day, and its time when it has one.</summary>
    public static string DayTime(
        DateOnly date,
        TimeOnly? time,
        DateOnly today)
    {
        return time is { } t
            ? $"{Day(date, today)} {Words.Time(t)}"
            : Day(date, today);
    }

    /// <summary>An instant as a day and time in the owner's zone.
    /// </summary>
    public static string Instant(
        DateTimeOffset instant,
        TimeZoneInfo zone,
        DateOnly today)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
        return DayTime(
            DateOnly.FromDateTime(local),
            TimeOnly.FromDateTime(local),
            today);
    }

    /// <summary>A repeat rule and its mode in words, or the raw rule
    /// when it cannot be said exactly.</summary>
    public static string Repeat(
        RecurrenceRule rule,
        RecurrenceMode mode)
    {
        if (RuleWords(rule) is not { } words)
        {
            return $"{rule} ({Words.Mode(mode)})";
        }

        var until = rule.Until is { } last
            ? " through " + last.ToString(
                "d MMM yyyy",
                CultureInfo.InvariantCulture)
            : "";
        return words + until + ModeWords(rule, mode);
    }

    /// <summary>What the rule says, or null for a rule these words
    /// would misdescribe.</summary>
    public static string? RuleWords(RecurrenceRule rule)
    {
        // The week start changes which weeks an interval counts.
        if (rule.WeekStart != DayOfWeek.Monday)
        {
            return null;
        }

        return rule.Frequency switch
        {
            Frequency.Daily => DailyWords(rule),
            Frequency.Weekly => WeeklyWords(rule),
            Frequency.Monthly => MonthlyWords(rule),
            Frequency.Yearly => YearlyWords(rule),
            _ => null,
        };
    }

    /// <summary>When a reminder fires, from its offset: by the clock
    /// for an item with no due time, which counts as due at 00:00,
    /// and from the due time for one that has it.</summary>
    public static string ReminderOffset(
        TimeSpan offset,
        TimeOnly? dueTime)
    {
        if (offset.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            return $"{offset:c} from the due time";
        }

        var minutes = (long)offset.TotalMinutes;
        return dueTime is null
            ? ClockWords(minutes)
            : FromDueTimeWords(minutes);
    }

    private static string ModeWords(
        RecurrenceRule rule,
        RecurrenceMode mode)
    {
        if (mode != RecurrenceMode.AfterCompletion)
        {
            return $", {Words.Mode(mode)}";
        }

        // The next one is a whole interval after completion, then the
        // first day the rule allows.
        var filtered = rule.ByDay.Count > 0
            || rule.ByMonthDay.Count > 0
            || rule.ByMonth.Count > 0;
        return filtered
            ? $", at least {IntervalWords(rule)} after completion"
            : ", counted from completion";
    }

    private static string IntervalWords(RecurrenceRule rule)
    {
        var unit = Unit(rule.Frequency);
        return rule.Interval == 1
            ? $"a {unit}"
            : $"{Number(rule.Interval)} {unit}s";
    }

    private static string Every(RecurrenceRule rule)
    {
        var unit = Unit(rule.Frequency);
        return rule.Interval == 1
            ? $"Every {unit}"
            : $"Every {Number(rule.Interval)} {unit}s";
    }

    private static string Unit(Frequency frequency)
    {
        return frequency switch
        {
            Frequency.Daily => "day",
            Frequency.Weekly => "week",
            Frequency.Monthly => "month",
            _ => "year",
        };
    }

    private static string? DailyWords(RecurrenceRule rule)
    {
        if (rule.ByMonth.Count > 0 || rule.ByMonthDay.Count > 0)
        {
            return null;
        }

        if (rule.ByDay.Count == 0)
        {
            return Every(rule);
        }

        var isWorkWeek = rule.Interval == 1
            && rule.ByDay.Count == WorkWeek.Length
            && rule.ByDay.All(
                d => d.Ordinal == 0 && WorkWeek.Contains(d.Day));
        return isWorkWeek ? "Every weekday" : null;
    }

    private static string? WeeklyWords(RecurrenceRule rule)
    {
        if (rule.ByMonth.Count > 0 || rule.ByMonthDay.Count > 0)
        {
            return null;
        }

        if (rule.ByDay.Count == 0)
        {
            return Every(rule);
        }

        var days = rule.ByDay
            .OrderBy(d => ((int)d.Day + 6) % 7)
            .Select(d => d.Day.ToString())
            .ToList();
        return $"{Every(rule)} on {Listed(days)}";
    }

    private static string? MonthlyWords(RecurrenceRule rule)
    {
        if (rule.ByMonth.Count > 0)
        {
            return null;
        }

        return (rule.ByMonthDay.Count, rule.ByDay.Count) switch
        {
            (0, 0) => Every(rule),
            (1, 0) => MonthDayWords(rule.ByMonthDay[0]) is { } day
                ? $"{Every(rule)} on {day}"
                : null,
            (0, 1) => NthWeekdayWords(rule.ByDay[0]) is { } weekday
                ? $"{Every(rule)} on {weekday}"
                : null,
            _ => null,
        };
    }

    private static string? YearlyWords(RecurrenceRule rule)
    {
        if (rule.ByMonth.Count == 0)
        {
            return Every(rule);
        }

        if (rule.ByMonth.Count != 1
            || rule.ByMonthDay.Count != 1
            || rule.ByDay.Count != 0)
        {
            return null;
        }

        // A day that some years lack is moved, which these words
        // would not say. 2025 is not a leap year.
        var month = rule.ByMonth[0];
        var day = rule.ByMonthDay[0];
        if (day < 1 || day > DateTime.DaysInMonth(2025, month))
        {
            return null;
        }

        var name = CultureInfo.InvariantCulture
            .DateTimeFormat
            .GetMonthName(month);
        return $"{Every(rule)} on {Number(day)} {name}";
    }

    private static string? MonthDayWords(int day)
    {
        if (day == -1)
        {
            return "the last day";
        }

        if (day < 1)
        {
            return null;
        }

        // The rule moves a day a month lacks back to its last day.
        return day > 28
            ? $"the {Ordinal(day)} (or the last day of a shorter month)"
            : $"the {Ordinal(day)}";
    }

    private static string? NthWeekdayWords(WeekdayRule weekday)
    {
        var nth = weekday.Ordinal switch
        {
            1 => "first",
            2 => "second",
            3 => "third",
            4 => "fourth",
            -1 => "last",
            _ => null,
        };
        return nth is null ? null : $"the {nth} {weekday.Day}";
    }

    private static string Ordinal(int number)
    {
        var suffix = (number % 100, number % 10) switch
        {
            (11 or 12 or 13, _) => "th",
            (_, 1) => "st",
            (_, 2) => "nd",
            (_, 3) => "rd",
            _ => "th",
        };
        return Number(number) + suffix;
    }

    private static string Listed(IReadOnlyList<string> names)
    {
        return names.Count == 1
            ? names[0]
            : string.Join(", ", names.Take(names.Count - 1))
                + " and "
                + names[^1];
    }

    private static string ClockWords(long minutes)
    {
        var day = (long)Math.Floor(minutes / 1440d);
        var clock = TimeOnly.MinValue.AddMinutes(minutes - (day * 1440));
        var which = day switch
        {
            0 => "on the due day",
            -1 => "the day before",
            1 => "the day after",
            < 0 => $"{Number(-day)} days before",
            _ => $"{Number(day)} days after",
        };
        return $"{Words.Time(clock)} {which}";
    }

    private static string FromDueTimeWords(long minutes)
    {
        if (minutes == 0)
        {
            return "At the due time";
        }

        var size = Math.Abs(minutes);
        var parts = new List<string>();
        AddPart(parts, size / 1440, "day");
        AddPart(parts, size % 1440 / 60, "hour");
        AddPart(parts, size % 60, "minute");
        var side = minutes < 0 ? "before" : "after";
        return $"{string.Join(' ', parts)} {side} the due time";
    }

    private static void AddPart(
        List<string> parts,
        long count,
        string unit)
    {
        if (count > 0)
        {
            parts.Add($"{Number(count)} {unit}{(count == 1 ? "" : "s")}");
        }
    }

    private static string Number(long number)
    {
        return number.ToString(CultureInfo.InvariantCulture);
    }
}
