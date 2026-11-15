using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Nudge.Web.Data;

/// <summary>
/// Turns the value of a column into JSON for the change log and
/// back. The JSON holds what the column holds: an instant as its UTC
/// text, a list or a repeat mode by name, a priority as its number,
/// an id as upper-case text. It goes through the same conversions
/// that <see cref="NudgeDbContext"/> stores the value with, so the
/// log cannot drift away from the tables.
/// </summary>
internal static class StoredJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // Leave apostrophes and accented letters as they are; the
        // log is read by people, and never ends up inside HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new UpperCaseGuidConverter() },
    };

    /// <summary>The JSON for a value of
    /// <paramref name="property"/>.</summary>
    public static JsonNode? FromValue(
        IProperty property,
        object? value)
    {
        if (value is null)
        {
            return null;
        }

        var converter = property.GetTypeMapping().Converter;
        var stored = converter is null
            ? value
            : converter.ConvertToProvider(value);
        return stored is null
            ? null
            : JsonSerializer.SerializeToNode(
                stored,
                stored.GetType(),
                Options);
    }

    /// <summary>The value of <paramref name="property"/> that
    /// <paramref name="json"/> was made from.</summary>
    public static object? ToValue(
        IProperty property,
        JsonNode? json)
    {
        if (json is null)
        {
            return null;
        }

        var converter = property.GetTypeMapping().Converter;
        var storedType = converter?.ProviderClrType
            ?? Nullable.GetUnderlyingType(property.ClrType)
            ?? property.ClrType;
        var stored = json.Deserialize(storedType, Options);
        return converter is null
            ? stored
            : converter.ConvertFromProvider(stored);
    }

    public static string ToText(JsonObject values)
    {
        return values.ToJsonString(Options);
    }

    /// <exception cref="JsonException">The text is not a JSON
    /// object.</exception>
    public static JsonObject Parse(string text)
    {
        return JsonNode.Parse(text) as JsonObject
            ?? throw new JsonException("Expected a JSON object.");
    }

    /// <summary>Ids are stored as upper-case text; written the same
    /// way here, an id in the log matches the id in its table.
    /// </summary>
    private sealed class UpperCaseGuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return reader.GetGuid();
        }

        public override void Write(
            Utf8JsonWriter writer,
            Guid value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(
                value.ToString("D").ToUpperInvariant());
        }
    }
}
