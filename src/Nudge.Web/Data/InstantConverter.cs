using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Nudge.Web.Data;

/// <summary>
/// Stores an instant as UTC text of fixed width, for example
/// <c>2026-11-13T04:30:00.0000000Z</c>. Sorting or comparing the
/// column as text then gives the same answer as comparing the
/// instants, which SQLite's own date-with-offset text does not.
/// The offset the value was created with is not kept: values are
/// read back in UTC and are equal to what was stored.
/// </summary>
internal sealed class InstantConverter()
    : ValueConverter<DateTimeOffset, string>(
        instant => ToText(instant),
        text => FromText(text))
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private static string ToText(DateTimeOffset instant)
    {
        return instant.UtcDateTime.ToString(
            Format,
            CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset FromText(string text)
    {
        return DateTimeOffset.ParseExact(
            text,
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
    }
}
