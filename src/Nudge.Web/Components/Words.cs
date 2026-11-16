using System.Globalization;
using Nudge.Core;

namespace Nudge.Web.Components;

/// <summary>How the web UI writes values, and reads them back from
/// its forms.</summary>
public static class Words
{
    public static string List(ItemState state)
    {
        return state switch
        {
            ItemState.Inbox => "Inbox",
            ItemState.NextAction => "Next action",
            ItemState.WaitingFor => "Waiting for",
            ItemState.Someday => "Someday/maybe",
            _ => state.ToString(),
        };
    }

    public static string Priority(Priority priority)
    {
        return priority == Nudge.Core.Priority.None
            ? "No priority"
            : $"{priority} priority";
    }

    public static string Mode(RecurrenceMode mode)
    {
        return mode switch
        {
            RecurrenceMode.FixedSchedule => "on a fixed schedule",
            RecurrenceMode.AfterCompletion => "after completion",
            _ => mode.ToString(),
        };
    }

    /// <summary>yyyy-MM-dd, as a date input takes it; empty for
    /// none.</summary>
    public static string Date(DateOnly? date)
    {
        return date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ?? "";
    }

    /// <summary>HH:mm, as a time input takes it; empty for none.
    /// </summary>
    public static string Time(TimeOnly? time)
    {
        return time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>An instant in the owner's time zone.</summary>
    public static string Instant(
        DateTimeOffset instant,
        TimeZoneInfo zone)
    {
        return TimeZoneInfo.ConvertTime(instant, zone)
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>A date from a form; empty is none.</summary>
    public static bool TryDate(
        string? text,
        out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (DateOnly.TryParseExact(
                text.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            date = parsed;
            return true;
        }

        return false;
    }

    /// <summary>A time from a form, HH:mm or HH:mm:ss; empty is none.
    /// </summary>
    public static bool TryTime(
        string? text,
        out TimeOnly? time)
    {
        time = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (TimeOnly.TryParseExact(
                text.Trim(),
                ["HH:mm", "HH:mm:ss"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            time = parsed;
            return true;
        }

        return false;
    }

    /// <summary>A GUID from a form; empty is none.</summary>
    public static bool TryGuid(
        string? text,
        out Guid? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (Guid.TryParse(text.Trim(), out var parsed))
        {
            id = parsed;
            return true;
        }

        return false;
    }

    /// <summary>A value of an enum from a form, by its name.
    /// </summary>
    public static bool TryEnum<T>(
        string? text,
        out T value)
        where T : struct, Enum
    {
        return Enum.TryParse(text?.Trim(), ignoreCase: false, out value)
            && Enum.IsDefined(value);
    }
}
