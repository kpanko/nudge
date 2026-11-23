using System.Globalization;
using Nudge.Core;

namespace Nudge.Web.Components;

/// <summary>An option of a preset list: what the form sends, and what
/// the owner reads.</summary>
public sealed record Choice(
    string Key,
    string Label);

/// <summary>A form's preset and custom fields, read: the value to
/// write, or why there is none.</summary>
public sealed record Chosen<T>(
    T? Value,
    string? Error);

/// <summary>The common repeat rules offered on the item page.
/// </summary>
public static class RepeatPresets
{
    public const string Custom = "custom";

    private const string WeeklyPrefix = "weekly-";
    private const string TwoWeeksPrefix = "2-weeks-";

    private static readonly string[] DayCodes =
        ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    private static readonly Dictionary<string, string> Fixed = new()
    {
        ["daily"] = "FREQ=DAILY",
        ["weekdays"] = "FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR",
        ["monthly"] = "FREQ=MONTHLY",
        ["3-months"] = "FREQ=MONTHLY;INTERVAL=3",
        ["6-months"] = "FREQ=MONTHLY;INTERVAL=6",
        ["yearly"] = "FREQ=YEARLY",
    };

    /// <summary>The presets, those on a weekday falling on
    /// <paramref name="day"/>'s.</summary>
    public static IReadOnlyList<Choice> For(DateOnly day)
    {
        var code = DayCodes[(int)day.DayOfWeek];
        string[] keys =
        [
            "daily",
            "weekdays",
            WeeklyPrefix + code,
            TwoWeeksPrefix + code,
            "monthly",
            "3-months",
            "6-months",
            "yearly",
        ];
        return keys
            .Select(
                key => new Choice(
                    key,
                    Plain.RuleWords(RecurrenceRule.Parse(Rule(key)!))!))
            .ToList();
    }

    /// <summary>The rule of a preset, or null if there is no such
    /// preset. The weekday is in the key, so a page left open keeps
    /// the day it showed.</summary>
    public static string? Rule(string? key)
    {
        if (key is null)
        {
            return null;
        }

        if (Fixed.TryGetValue(key, out var rule))
        {
            return rule;
        }

        if (key.StartsWith(WeeklyPrefix, StringComparison.Ordinal)
            && DayCodes.Contains(key[WeeklyPrefix.Length..]))
        {
            return $"FREQ=WEEKLY;BYDAY={key[WeeklyPrefix.Length..]}";
        }

        if (key.StartsWith(TwoWeeksPrefix, StringComparison.Ordinal)
            && DayCodes.Contains(key[TwoWeeksPrefix.Length..]))
        {
            return "FREQ=WEEKLY;INTERVAL=2;BYDAY="
                + key[TwoWeeksPrefix.Length..];
        }

        return null;
    }

    /// <summary>The option to show for a rule: its preset, or Custom
    /// for any other rule; empty when there is no rule yet.</summary>
    public static string Selected(
        RecurrenceRule? rule,
        DateOnly day)
    {
        if (rule is null)
        {
            return "";
        }

        var text = rule.ToString();
        return For(day)
            .FirstOrDefault(choice => Rule(choice.Key) == text)
            ?.Key ?? Custom;
    }

    /// <summary>The rule a sent form asks for. The typed rule counts
    /// only under Custom; typed under a preset, it is refused rather
    /// than dropped.</summary>
    public static Chosen<string> Resolve(
        string? preset,
        string? typed,
        string? shown)
    {
        var rule = typed?.Trim() ?? "";
        if (preset == Custom)
        {
            return rule.Length > 0
                ? new Chosen<string>(rule, null)
                : new Chosen<string>(null, "Type a rule for Custom.");
        }

        if (Rule(preset) is not { } presetRule)
        {
            return new Chosen<string>(null, "Choose how often it repeats.");
        }

        return rule == (shown?.Trim() ?? "")
            ? new Chosen<string>(presetRule, null)
            : new Chosen<string>(
                null,
                "Choose Custom to use the rule you typed.");
    }
}

/// <summary>The common reminders offered on the item page, each
/// mapped to the minutes from the due time that the write takes.
/// </summary>
public static class ReminderPresets
{
    public const string Custom = "custom";

    private const int MinutesPerDay = 1440;

    private static readonly (string Key, int Day, TimeOnly Clock)[] Clock =
    [
        ("day-0900", 0, new TimeOnly(9, 0)),
        ("day-before-1800", -1, new TimeOnly(18, 0)),
        ("day-before-0900", -1, new TimeOnly(9, 0)),
        ("week-before-0900", -7, new TimeOnly(9, 0)),
    ];

    private static readonly (string Key, int Before)[] Relative =
    [
        ("at-due", 0),
        ("15-before", 15),
        ("60-before", 60),
        ("1-day-before", MinutesPerDay),
    ];

    /// <summary>The presets that fit an item's due time. Ones from
    /// the due time need it to have one; ones by the clock need it
    /// to be on a whole minute, as offsets are.</summary>
    public static IReadOnlyList<Choice> For(TimeOnly? dueTime)
    {
        var choices = new List<Choice>();
        if (dueTime is not null)
        {
            choices.AddRange(
                Relative.Select(
                    r => new Choice(
                        r.Key,
                        Plain.ReminderOffset(
                            TimeSpan.FromMinutes(-r.Before),
                            dueTime))));
        }

        if (OnAWholeMinute(dueTime))
        {
            choices.AddRange(
                Clock.Select(
                    c => new Choice(
                        c.Key,
                        Plain.ReminderOffset(
                            TimeSpan.FromMinutes(
                                (c.Day * MinutesPerDay) + Minutes(c.Clock)),
                            null))));
        }

        return choices;
    }

    /// <summary>The offset of a preset for this due time, or null if
    /// the preset does not fit it. An item with no due time counts
    /// as due at 00:00.</summary>
    public static int? Offset(
        string? key,
        TimeOnly? dueTime)
    {
        foreach (var (name, before) in Relative)
        {
            if (name == key)
            {
                return dueTime is null ? null : -before;
            }
        }

        foreach (var (name, day, clock) in Clock)
        {
            if (name == key)
            {
                return OnAWholeMinute(dueTime)
                    ? (day * MinutesPerDay)
                        + Minutes(clock)
                        - (dueTime is { } due ? Minutes(due) : 0)
                    : null;
            }
        }

        return null;
    }

    /// <summary>The offset a sent form asks for, worked out from the
    /// item's current due time. Typed minutes count only under
    /// Custom; typed under a preset, they are refused rather than
    /// dropped.</summary>
    public static Chosen<int?> Resolve(
        string? preset,
        string? typed,
        TimeOnly? dueTime)
    {
        var minutes = typed?.Trim() ?? "";
        if (preset == Custom)
        {
            return int.TryParse(
                    minutes,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                ? new Chosen<int?>(parsed, null)
                : new Chosen<int?>(
                    null,
                    "Give the minutes as a whole number.");
        }

        if (minutes.Length > 0)
        {
            return new Chosen<int?>(
                null,
                "Choose Custom to use the minutes you typed.");
        }

        return Offset(preset, dueTime) is { } offset
            ? new Chosen<int?>(offset, null)
            : new Chosen<int?>(
                null,
                "That choice does not fit this item's due time."
                + " Choose again.");
    }

    private static bool OnAWholeMinute(TimeOnly? time)
    {
        return time is not { } t || t.Ticks % TimeSpan.TicksPerMinute == 0;
    }

    private static int Minutes(TimeOnly time)
    {
        return (time.Hour * 60) + time.Minute;
    }
}
