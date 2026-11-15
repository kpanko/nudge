using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Nudge.Web.Api;

/// <summary>
/// The OpenAPI document, served at <c>/openapi/v1.json</c>. It is
/// built from the endpoints themselves; this adds what every write
/// takes and does, which the endpoints declare through
/// <see cref="WriteEndpoint"/> rather than one by one.
/// </summary>
internal static class OpenApiSetup
{
    public const string DocumentPath = "/openapi/v1.json";

    public static IServiceCollection AddNudgeOpenApi(
        this IServiceCollection services)
    {
        return services.AddOpenApi(
            options =>
            {
                options.AddDocumentTransformer(DescribeDocument);
                options.AddOperationTransformer(DescribeWrite);
                options.AddSchemaTransformer(DescribeInstant);
            });
    }

    private static Task DescribeDocument(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info.Title = "Nudge";
        document.Info.Description =
            "A single-user GTD to-do list. Every write is recorded in"
            + " the change log and can be undone (POST"
            + " /api/changes/{id}/undo). Every write takes"
            + " ?dryRun=true and an Idempotency-Key header. Requests"
            + " are read strictly: an unknown query parameter or"
            + " property is a 400, never ignored.";
        return Task.CompletedTask;
    }

    /// <summary>Instants are read and written by a converter of the
    /// API's own, which hides from the generator that they are text.
    /// </summary>
    private static Task DescribeInstant(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(DateTimeOffset)
            || type == typeof(DateTimeOffset?))
        {
            schema.Type = type == typeof(DateTimeOffset)
                ? JsonSchemaType.String
                : JsonSchemaType.String | JsonSchemaType.Null;
            schema.Format = "date-time";
        }

        return Task.CompletedTask;
    }

    private static Task DescribeWrite(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var write = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<WriteEndpoint>()
            .SingleOrDefault();
        if (write is null)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(
            new OpenApiParameter
            {
                Name = WriteRunner.DryRunParameter,
                In = ParameterLocation.Query,
                Required = false,
                Description =
                    "true to carry the write out and roll it back:"
                    + " nothing is saved, and the response (always 200)"
                    + " shows what would have been, with change.id null."
                    + " Ids of anything a dry run creates are not real."
                    + " Only true or false; any other query parameter"
                    + " is refused.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.Boolean },
            });
        operation.Parameters.Add(
            new OpenApiParameter
            {
                Name = WriteRunner.KeyHeader,
                In = ParameterLocation.Header,
                Required = false,
                Description =
                    "Makes the write safe to retry. The first request"
                    + " with a key is carried out and its response kept;"
                    + " the same request sent again gets that response"
                    + $" back, with {WriteRunner.ReplayedHeader}: true,"
                    + " and nothing is done. The same key with another"
                    + " method, path or body is refused with 422. A"
                    + " request that fails is not kept. A dry run keeps"
                    + " nothing, and counts as the same request as the"
                    + " real one.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    MinLength = 1,
                    MaxLength =
                        Data.IdempotencyRecordConfiguration.MaxKeyLength,
                },
            });

        foreach (var (status, response) in operation.Responses ?? [])
        {
            if (status.StartsWith('2') && response is OpenApiResponse real)
            {
                real.Headers ??= new Dictionary<string, IOpenApiHeader>();
                real.Headers[WriteRunner.ReplayedHeader] = new OpenApiHeader
                {
                    Description =
                        "true when this is the kept response to an"
                        + " earlier request with the same idempotency"
                        + " key.",
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = [JsonValue.Create("true")],
                    },
                };
            }
        }

        operation.Description = string.Join(
            " ",
            new[]
            {
                operation.Description,
                $"Recorded in the change log as \"{write.Description}\".",
            }.Where(text => !string.IsNullOrEmpty(text)));
        return Task.CompletedTask;
    }
}
