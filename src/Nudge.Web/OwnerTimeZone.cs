namespace Nudge.Web;

/// <summary>
/// The owner's time zone. It decides what day it is when an item is
/// completed, and so which occurrence comes next; a wrong zone would
/// move items by a day without anyone noticing. It is set with
/// <c>Nudge:TimeZone</c> (environment variable
/// <c>Nudge__TimeZone</c>) to an IANA id such as
/// <c>Europe/London</c>.
/// </summary>
public sealed class OwnerTimeZone
{
    public const string Setting = "Nudge:TimeZone";

    private OwnerTimeZone(
        TimeZoneInfo zone,
        bool configured)
    {
        Zone = zone;
        Configured = configured;
    }

    public TimeZoneInfo Zone { get; }

    /// <summary>False when the machine's own zone is used because
    /// nothing was set, which only happens in Development.</summary>
    public bool Configured { get; }

    /// <summary>
    /// The zone from configuration. Outside Development it must be
    /// set; in Development an unset value falls back to the
    /// machine's local zone.
    /// </summary>
    /// <exception cref="InvalidOperationException">It is not set
    /// where it has to be, or it is not an IANA id known on this
    /// machine.</exception>
    public static OwnerTimeZone From(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var id = configuration[Setting]?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            if (environment.IsDevelopment())
            {
                return new OwnerTimeZone(TimeZoneInfo.Local, false);
            }

            throw new InvalidOperationException(
                $"Set {Setting} (environment variable Nudge__TimeZone)"
                + " to the owner's IANA time zone, such as"
                + " Europe/London. It decides what day an item was"
                + " completed on.");
        }

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e
            is TimeZoneNotFoundException
            or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"{Setting} is \"{id}\", which is not a time zone"
                + " known on this machine.",
                e);
        }

        if (!zone.HasIanaId)
        {
            throw new InvalidOperationException(
                $"{Setting} is \"{id}\". Use an IANA id, such as"
                + " Europe/London, so that the setting means the same"
                + " on every system.");
        }

        return new OwnerTimeZone(zone, true);
    }

    public override string ToString()
    {
        return Configured
            ? Zone.Id
            : $"{Zone.Id} (the machine's own; {Setting} is not set)";
    }
}
