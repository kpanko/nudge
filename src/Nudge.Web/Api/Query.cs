using System.Globalization;
using System.Text.Json;

namespace Nudge.Web.Api;

/// <summary>
/// The query of a read, checked as strictly as a write's: a
/// parameter that is not one of those named, given twice, or not of
/// its type is a 400, so a misspelt filter cannot quietly widen what
/// comes back.
/// </summary>
internal sealed class Query
{
    private readonly IQueryCollection _query;

    public Query(
        IQueryCollection query,
        params string[] allowed)
    {
        foreach (var (name, values) in query)
        {
            if (!allowed.Contains(name, StringComparer.Ordinal))
            {
                throw ApiProblem.Invalid(
                    name,
                    allowed.Length == 0
                        ? "Unknown query parameter. This takes none."
                        : "Unknown query parameter. This takes "
                            + string.Join(", ", allowed) + ".");
            }

            if (values.Count != 1)
            {
                throw ApiProblem.Invalid(name, "Give it once.");
            }
        }

        _query = query;
    }

    public Guid? Guid(string name)
    {
        return Text(name) is { } text
            ? System.Guid.TryParse(text, out var id)
                ? id
                : throw ApiProblem.Invalid(name, "Not an id.")
            : null;
    }

    public bool? Bool(string name)
    {
        return Text(name) switch
        {
            null => null,
            "true" => true,
            "false" => false,
            _ => throw ApiProblem.Invalid(name, "Must be true or false."),
        };
    }

    public long? Long(string name)
    {
        return Text(name) is { } text
            ? long.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : throw ApiProblem.Invalid(name, "Not a whole number.")
            : null;
    }

    /// <summary>A value by the name it has in JSON, such as
    /// <c>nextAction</c>.</summary>
    public T? Enum<T>(string name)
        where T : struct, System.Enum
    {
        if (Text(name) is not { } text)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(
                JsonSerializer.Serialize(text),
                JsonSetup.Options);
        }
        catch (JsonException)
        {
            throw ApiProblem.Invalid(
                name,
                "Not one of: "
                + string.Join(
                    ", ",
                    System.Enum.GetValues<T>()
                        .Select(
                            value => JsonSerializer.Serialize(
                                value,
                                JsonSetup.Options)))
                + ".");
        }
    }

    private string? Text(string name)
    {
        return _query.TryGetValue(name, out var values) ? values[0] : null;
    }
}
