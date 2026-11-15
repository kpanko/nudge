using System.Globalization;
using System.Text.Json;

namespace Nudge.Web.Api;

/// <summary>
/// A query parameter a read takes. The endpoint declares its
/// parameters once, with <see cref="QueryRules.Takes"/>; the same
/// declaration is what <see cref="Query"/> accepts and what the
/// OpenAPI document lists, so the two cannot drift apart.
/// </summary>
/// <param name="Type">bool, Guid, long, or an enum, which is given
/// by the name it has in JSON.</param>
public sealed record QueryParameter(
    string Name,
    Type Type,
    string Description);

/// <summary>The query parameters a read endpoint takes; endpoint
/// metadata.</summary>
public sealed record QueryParameters(IReadOnlyList<QueryParameter> All);

internal static class QueryRules
{
    /// <summary>
    /// Declares the query parameters the endpoint takes (none if
    /// none are given) and that anything else is a 400.
    /// </summary>
    public static RouteHandlerBuilder Takes(
        this RouteHandlerBuilder builder,
        params QueryParameter[] parameters)
    {
        return builder
            .WithMetadata(new QueryParameters(parameters))
            .ProducesValidationProblem();
    }
}

/// <summary>
/// The query of a read, checked as strictly as a write's: a
/// parameter the endpoint does not declare, one given twice, or one
/// not of its type is a 400, so a misspelt filter cannot quietly
/// widen what comes back.
/// </summary>
public sealed class Query
{
    private readonly IQueryCollection _query;
    private readonly IReadOnlyList<QueryParameter> _declared;

    public Query(HttpContext http)
    {
        _declared = http.GetEndpoint()
                ?.Metadata
                .GetMetadata<QueryParameters>()
                ?.All
            ?? throw new InvalidOperationException(
                "The endpoint does not declare its query parameters.");
        var allowed = _declared.Select(p => p.Name).ToList();
        foreach (var (name, values) in http.Request.Query)
        {
            if (!allowed.Contains(name, StringComparer.Ordinal))
            {
                throw ApiProblem.Invalid(
                    name,
                    allowed.Count == 0
                        ? "Unknown query parameter. This takes none."
                        : "Unknown query parameter. This takes "
                            + string.Join(", ", allowed) + ".");
            }

            if (values.Count != 1)
            {
                throw ApiProblem.Invalid(name, "Give it once.");
            }
        }

        _query = http.Request.Query;
    }

    public Guid? Guid(string name)
    {
        return Text<Guid>(name) is { } text
            ? System.Guid.TryParse(text, out var id)
                ? id
                : throw ApiProblem.Invalid(name, "Not an id.")
            : null;
    }

    public bool? Bool(string name)
    {
        return Text<bool>(name) switch
        {
            null => null,
            "true" => true,
            "false" => false,
            _ => throw ApiProblem.Invalid(name, "Must be true or false."),
        };
    }

    public long? Long(string name)
    {
        return Text<long>(name) is { } text
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
        if (Text<T>(name) is not { } text)
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
                "Not one of: " + string.Join(", ", Names<T>()) + ".");
        }
    }

    /// <summary>The names an enum's values have in JSON.</summary>
    public static IEnumerable<string> Names<T>()
        where T : struct, System.Enum
    {
        return Names(typeof(T));
    }

    /// <inheritdoc cref="Names{T}"/>
    public static IEnumerable<string> Names(Type type)
    {
        return System.Enum.GetValues(type)
            .Cast<object>()
            .Select(
                value => JsonSerializer
                    .SerializeToElement(value, type, JsonSetup.Options)
                    .GetString()!);
    }

    private string? Text<T>(string name)
    {
        // A handler reading a parameter it did not declare would
        // take what the document does not show; that is a bug.
        if (!_declared.Any(p => p.Name == name && p.Type == typeof(T)))
        {
            throw new InvalidOperationException(
                $"The query parameter {name} is not declared as"
                + $" {typeof(T).Name}.");
        }

        return _query.TryGetValue(name, out var values) ? values[0] : null;
    }
}
