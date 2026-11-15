using System.Text.Json;
using Nudge.Web.Data;

namespace Nudge.Web.Api;

/// <summary>
/// What the work of one write endpoint is given: the request's
/// context, which a transaction already holds, the parsed body, and
/// the time and zone to use.
/// </summary>
public sealed class Write<TBody>
{
    private readonly IReadOnlySet<string> _present;
    private readonly HttpContext _http;

    internal Write(
        NudgeDbContext db,
        TBody body,
        IReadOnlySet<string> present,
        HttpContext http,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        Db = db;
        Body = body;
        _present = present;
        _http = http;
        Now = now;
        Zone = zone;
    }

    public NudgeDbContext Db { get; }

    public TBody Body { get; }

    public DateTimeOffset Now { get; }

    /// <summary>The owner's time zone.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>
    /// Whether the body named the property, given by its C# name,
    /// even with null. This is how a PATCH tells "leave it" from
    /// "clear it".
    /// </summary>
    public bool Has(string propertyName)
    {
        return _present.Contains(JsonName(propertyName));
    }

    /// <summary>The name a property has in JSON.</summary>
    public static string JsonName(string propertyName)
    {
        return JsonNamingPolicy.CamelCase.ConvertName(propertyName);
    }

    /// <summary>A route value the route's constraint has already
    /// checked to be a GUID.</summary>
    public Guid RouteGuid(string name)
    {
        return Guid.Parse((string)_http.GetRouteValue(name)!);
    }

    /// <summary>A route value the route's constraint has already
    /// checked to be a whole number.</summary>
    public long RouteLong(string name)
    {
        return long.Parse(
            (string)_http.GetRouteValue(name)!,
            System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>What the work of a write endpoint comes to.</summary>
/// <param name="Status">The status code when it is carried out for
/// real; a dry run always answers 200.</param>
/// <param name="Data">Makes the resource to return, after saving.
/// </param>
/// <param name="Change">The change it recorded, if it did not save
/// through the context in the usual way (an undo).</param>
public sealed record WriteOutcome<TData>(
    int Status,
    Func<TData?> Data,
    Change? Change = null);

/// <summary>Short ways to make a <see cref="WriteOutcome{TData}"/>.
/// </summary>
internal static class WriteOutcome
{
    public static WriteOutcome<T> Ok<T>(Func<T?> data)
    {
        return new WriteOutcome<T>(StatusCodes.Status200OK, data);
    }

    public static WriteOutcome<T> Created<T>(Func<T?> data)
    {
        return new WriteOutcome<T>(StatusCodes.Status201Created, data);
    }

    public static WriteOutcome<T> Deleted<T>()
    {
        return new WriteOutcome<T>(StatusCodes.Status200OK, () => default);
    }
}
