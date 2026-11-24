using System.Text.RegularExpressions;
using Nudge.Core;
using Nudge.Web.Api;

namespace Nudge.Web.Components;

/// <summary>What a captured line asks for: its title, and the due
/// date or repeat its opening words gave, if any.</summary>
public sealed record CapturedText(
    string Title,
    DueInput? Due,
    RepeatInput? Repeat);

/// <summary>
/// The basic date words a capture understands, only at the start of
/// the line and only with a title after them: "every friday eat a
/// snack", "tomorrow call the bank". Anything else is a plain title.
/// Repeats are on a fixed schedule. A weekday means the next one,
/// today included.
/// </summary>
public static class CapturePhrase
{
    /// <summary>The hidden field the page sets to 1 when the owner
    /// said the opening words are not a date.</summary>
    public const string PlainField = "plainTitle";

    private const string Days =
        "sunday|monday|tuesday|wednesday|thursday|friday|saturday";

    /// <summary>The opening date words, for the page to highlight as
    /// they are typed. Valid in both .NET and JavaScript.</summary>
    public const string Words =
        @"^(?:every\s+(?:day|weekday|month|year|"
        + Days
        + @"|(?:week|2\s+weeks)(?:\s+on\s+(?:"
        + Days
        + @"))?|(?:3|6)\s+months)|today|tomorrow|"
        + Days
        + @")(?=\s)";

    private const string Weekdays = "MO,TU,WE,TH,FR";

    private const RegexOptions Options =
        RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.Singleline;

    private static readonly Regex Repeating = new(
        @"^every\s+(?:"
        + @"(?<unit>day|weekday|month|year)"
        + $"|(?<day>{Days})"
        + $@"|(?<weeks>week|2\s+weeks)(?:\s+on\s+(?<day>{Days}))?"
        + @"|(?<months>3|6)\s+months"
        + @")\s+(?<title>\S.*)$",
        Options);

    private static readonly Regex OneOff = new(
        $@"^(?<when>today|tomorrow|{Days})\s+(?<title>\S.*)$",
        Options);

    /// <summary>Reads a sent capture form, keeping the line as a plain
    /// title if the owner dismissed its date words.</summary>
    public static CapturedText FromForm(
        IFormCollection form,
        DateOnly today)
    {
        var text = form["title"].ToString().Trim();
        return form[PlainField] == "1"
            ? new CapturedText(text, null, null)
            : Read(text, today);
    }

    /// <summary>Reads a trimmed captured line.</summary>
    public static CapturedText Read(
        string text,
        DateOnly today)
    {
        var repeating = Repeating.Match(text);
        if (repeating.Success)
        {
            var (rule, start) = RuleOf(repeating, today);
            return new CapturedText(
                repeating.Groups["title"].Value,
                null,
                new RepeatInput
                {
                    Rule = rule,
                    Mode = RecurrenceMode.FixedSchedule,
                    Start = start,
                });
        }

        var oneOff = OneOff.Match(text);
        if (oneOff.Success)
        {
            return new CapturedText(
                oneOff.Groups["title"].Value,
                new DueInput
                {
                    Date = DayOf(oneOff.Groups["when"].Value, today),
                },
                null);
        }

        return new CapturedText(text, null, null);
    }

    private static (string Rule, DateOnly Start) RuleOf(
        Match match,
        DateOnly today)
    {
        if (match.Groups["months"].Success)
        {
            var months = match.Groups["months"].Value;
            return ($"FREQ=MONTHLY;INTERVAL={months}", today);
        }

        if (match.Groups["unit"].Success)
        {
            var unit = match.Groups["unit"].Value.ToUpperInvariant();
            return unit switch
            {
                "DAY" => ("FREQ=DAILY", today),
                "WEEKDAY" => ($"FREQ=DAILY;BYDAY={Weekdays}", today),
                "MONTH" => ("FREQ=MONTHLY", today),
                _ => ("FREQ=YEARLY", today),
            };
        }

        var day = match.Groups["day"].Success
            ? Enum.Parse<DayOfWeek>(match.Groups["day"].Value, true)
            : today.DayOfWeek;
        var everyTwo = match.Groups["weeks"].Value.StartsWith('2');
        var code = day.ToString()[..2].ToUpperInvariant();

        // The series starts on the day itself, so that an every-two-
        // weeks rule counts its weeks from the first occurrence.
        return (
            everyTwo
                ? $"FREQ=WEEKLY;INTERVAL=2;BYDAY={code}"
                : $"FREQ=WEEKLY;BYDAY={code}",
            NextOnOrAfter(today, day));
    }

    private static DateOnly DayOf(
        string word,
        DateOnly today)
    {
        if (word.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            return today;
        }

        return word.Equals("tomorrow", StringComparison.OrdinalIgnoreCase)
            ? today.AddDays(1)
            : NextOnOrAfter(today, Enum.Parse<DayOfWeek>(word, true));
    }

    private static DateOnly NextOnOrAfter(
        DateOnly today,
        DayOfWeek day)
    {
        return today.AddDays(((int)day - (int)today.DayOfWeek + 7) % 7);
    }
}
