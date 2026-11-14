using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nudge.Web.Api;

/// <summary>
/// How the API reads and writes JSON. Reading is strict on purpose:
/// a property that is misspelt, in the wrong case, of the wrong type
/// or given twice is an error, never silently ignored, because an
/// ignored input is how a client wrecks data while believing it did
/// something else.
/// </summary>
public static class JsonSetup
{
    /// <summary>The options the API uses, for code that reads or
    /// writes JSON itself rather than through the framework.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>Sets <paramref name="options"/> up as the API's.
    /// </summary>
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = false;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        options.AllowDuplicateProperties = false;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
        options.Converters.Add(new InstantConverter());
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>
    /// Instants are written in UTC with a Z, such as
    /// <c>2026-11-14T09:30:00Z</c>. On input any offset is accepted,
    /// but one must be given: a time without an offset could mean
    /// any instant.
    /// </summary>
    private sealed class InstantConverter : JsonConverter<DateTimeOffset>
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (text is null
                || !DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var value)
                || !HasOffset(text))
            {
                throw new JsonException(
                    "Expected an instant with an offset, such as"
                    + " 2026-11-14T09:30:00Z or"
                    + " 2026-11-14T04:30:00-05:00.");
            }

            return value.ToUniversalTime();
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(
                value.UtcDateTime.ToString(
                    Format,
                    CultureInfo.InvariantCulture));
        }

        private static bool HasOffset(string text)
        {
            if (text.EndsWith('Z') || text.EndsWith('z'))
            {
                return true;
            }

            // ...+05:00 or ...-05:00 after the time.
            var time = text.IndexOf('T', StringComparison.Ordinal);
            return time > 0
                && text.IndexOfAny(['+', '-'], time) > 0;
        }
    }
}
