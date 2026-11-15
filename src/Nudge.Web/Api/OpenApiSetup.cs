using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.OpenApi;

namespace Nudge.Web.Api;

/// <summary>
/// The OpenAPI document, served at <c>/openapi/v1.json</c>. It is
/// built from the endpoints themselves; this adds what the
/// generator cannot see in them: the route's parameters (a write's
/// handler does not take them as arguments), the query parameters a
/// read declares with <see cref="QueryRules.Takes"/>, and what every
/// write takes and does, which the endpoints declare through
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
                options.AddOperationTransformer(DescribeRoute);
                options.AddOperationTransformer(DescribeQuery);
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

    /// <summary>
    /// Every parameter in the route, as a required path parameter of
    /// the type its constraint gives it. The route is the endpoint's
    /// own, found by its name, which is the operation id.
    /// </summary>
    private static Task DescribeRoute(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var name = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IEndpointNameMetadata>()
            .SingleOrDefault()
            ?.EndpointName;
        if (name is null)
        {
            return Task.CompletedTask;
        }

        var route = context.ApplicationServices
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(
                endpoint => endpoint.Metadata
                    .GetMetadata<IEndpointNameMetadata>()
                    ?.EndpointName == name)
            .RoutePattern;
        if (route.Parameters.Count == 0)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];
        foreach (var parameter in route.Parameters)
        {
            var existing = operation.Parameters
                .Where(
                    p => p.In == ParameterLocation.Path
                        && p.Name == parameter.Name)
                .ToList();
            foreach (var each in existing)
            {
                operation.Parameters.Remove(each);
            }

            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = parameter.Name,
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = RouteSchema(parameter),
                });
        }

        return Task.CompletedTask;
    }

    private static OpenApiSchema RouteSchema(
        RoutePatternParameterPart parameter)
    {
        var constraints = parameter.ParameterPolicies
            .Select(policy => policy.Content)
            .ToList();
        if (constraints.Contains("guid"))
        {
            return new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "uuid",
            };
        }

        if (constraints.Contains("long"))
        {
            return new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int64",
            };
        }

        return new OpenApiSchema { Type = JsonSchemaType.String };
    }

    /// <summary>The query parameters a read declares, which are the
    /// ones it accepts.</summary>
    private static Task DescribeQuery(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var declared = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<QueryParameters>()
            .SingleOrDefault();
        if (declared is null)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];
        foreach (var parameter in declared.All)
        {
            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = parameter.Name,
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = parameter.Description,
                    Schema = QuerySchema(parameter.Type),
                });
        }

        operation.Description = string.Join(
            " ",
            new[]
            {
                operation.Description,
                "Any other query parameter is refused with 400.",
            }.Where(text => !string.IsNullOrEmpty(text)));
        return Task.CompletedTask;
    }

    private static OpenApiSchema QuerySchema(Type type)
    {
        if (type.IsEnum)
        {
            return new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = Query.Names(type)
                    .Select(name => (JsonNode)JsonValue.Create(name)!)
                    .ToList(),
            };
        }

        if (type == typeof(bool))
        {
            return new OpenApiSchema { Type = JsonSchemaType.Boolean };
        }

        if (type == typeof(Guid))
        {
            return new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "uuid",
            };
        }

        if (type == typeof(long))
        {
            return new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int64",
            };
        }

        throw new NotSupportedException(
            $"A query parameter of type {type.Name} is not described.");
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
                    + " nothing is saved, and the response, 200, shows"
                    + " what would have been, with dryRun true and"
                    + " change.id null. Ids of anything a dry run"
                    + " creates are not real. One exception: with an"
                    + " Idempotency-Key already used for this request,"
                    + " the kept response of the real request is"
                    + " returned instead, with its own status, dryRun"
                    + " false and Idempotent-Replayed: true, and"
                    + " nothing is done. Only true or false; any other"
                    + " query parameter is refused.",
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
                    + " real one: a dry run with a key the real request"
                    + " already used gets the real request's kept"
                    + " response (dryRun false), not a new dry run.",
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
