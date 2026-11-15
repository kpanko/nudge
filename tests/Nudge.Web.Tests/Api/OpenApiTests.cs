using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nudge.Web.Api;

namespace Nudge.Web.Tests.Api;

/// <summary>
/// The OpenAPI document describes every endpoint of the API, and
/// describes the bodies the endpoints actually accept.
/// </summary>
public sealed class OpenApiTests : IDisposable
{
    /// <summary>
    /// A value for every property each PATCH body is documented to
    /// take. A property added to a PATCH body without a value here
    /// fails <see cref="Patch_TakesEveryDocumentedProperty"/>, which
    /// sends each one and checks it is applied.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>>
        PatchValues = new()
        {
            ["/api/items/{id}"] = new()
            {
                ["title"] = "\"Water the ferns\"",
                ["notes"] = "\"Twice a week.\"",
                ["list"] = "\"someday\"",
                ["priority"] = "\"high\"",
                ["projectId"] = "null",
                ["deferUntil"] = "\"2026-11-18\"",
                ["doNotAutoReschedule"] = "true",
            },
            ["/api/projects/{id}"] = new()
            {
                ["name"] = "\"Kitchen\"",
                ["parentId"] = "null",
            },
            ["/api/contexts/{id}"] = new()
            {
                ["name"] = "\"telephone\"",
            },
        };

    /// <summary>The name of the security scheme in the document.
    /// </summary>
    private const string BearerScheme = "bearer";

    private readonly Api _api = new();

    public void Dispose()
    {
        _api.Dispose();
    }

    private async Task<JsonNode> DocumentAsync()
    {
        var reply = await _api.SendAsync("GET", "/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, reply.Status);
        return reply.Json;
    }

    private static JsonNode Resolve(
        JsonNode document,
        JsonNode schema)
    {
        var reference = schema["$ref"]?.GetValue<string>();
        return reference is null
            ? schema
            : document["components"]!["schemas"]![
                reference["#/components/schemas/".Length..]]!;
    }

    /// <summary>"/api/items/{id:guid}" as the document writes it.
    /// </summary>
    private static string DocumentPath(string route)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            route,
            "{([a-zA-Z]+):[a-z]+}",
            "{$1}");
    }

    [Fact]
    public async Task Document_DescribesEveryApiEndpoint()
    {
        var document = await DocumentAsync();
        var paths = document["paths"]!;
        var endpoints = _api.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(
                e => e.RoutePattern.RawText!.StartsWith(
                    ApiRoutes.Prefix + "/",
                    StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(endpoints);

        foreach (var endpoint in endpoints)
        {
            var path = DocumentPath(endpoint.RoutePattern.RawText!);
            var method = endpoint.Metadata
                .GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!
                .HttpMethods
                .Single()
                .ToLowerInvariant();
            var operation = paths[path]?[method];
            Assert.True(operation is not null, $"{method} {path} is missing.");
            Assert.False(
                string.IsNullOrEmpty(operation["operationId"]?.GetValue<string>()),
                $"{method} {path} has no operationId.");
            Assert.False(
                string.IsNullOrEmpty(operation["summary"]?.GetValue<string>()),
                $"{method} {path} has no summary.");

            var responses = operation["responses"]!.AsObject();

            // Every request to the API needs the token.
            Assert.True(
                responses.ContainsKey("401"),
                $"{method} {path} does not document 401.");
            var security = operation["security"]?.AsArray();
            Assert.True(
                security is [{ } requirement]
                    && requirement.AsObject().ContainsKey(
                        BearerScheme),
                $"{method} {path} does not require the token.");

            var success = responses
                .Where(r => r.Key.StartsWith('2'))
                .Select(r => r.Value!)
                .ToList();
            Assert.NotEmpty(success);
            Assert.All(
                success,
                r => Assert.NotNull(
                    r["content"]?["application/json"]?["schema"]));

            if (endpoint.Metadata.GetMetadata<WriteEndpoint>() is null)
            {
                continue;
            }

            var parameters = operation["parameters"]!.AsArray()
                .Select(
                    p => (
                        p!["name"]!.GetValue<string>(),
                        p["in"]!.GetValue<string>()))
                .ToList();
            Assert.Contains(("dryRun", "query"), parameters);
            Assert.Contains(("Idempotency-Key", "header"), parameters);
            foreach (var status in new[] { "200", "400", "404", "409", "422" })
            {
                Assert.True(
                    responses.ContainsKey(status),
                    $"{method} {path} does not document {status}.");
            }

            Assert.All(
                success,
                r => Assert.NotNull(r["headers"]?["Idempotent-Replayed"]));
        }
    }

    // The expectations come from the app: an endpoint carries the
    // token requirement exactly when it does not allow anonymous
    // requests.
    [Fact]
    public async Task Document_RequiresTheTokenWhereTheAppDoes()
    {
        var document = await DocumentAsync();
        var scheme =
            document["components"]!["securitySchemes"]![BearerScheme]!;
        Assert.Equal("http", scheme["type"]!.GetValue<string>());
        Assert.Equal("bearer", scheme["scheme"]!.GetValue<string>());

        var endpoints = _api.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToList();
        var described = 0;
        foreach (var endpoint in endpoints)
        {
            var path = DocumentPath(endpoint.RoutePattern.RawText!);
            var method = endpoint.Metadata
                .GetMetadata<HttpMethodMetadata>()!
                .HttpMethods
                .Single()
                .ToLowerInvariant();
            if (document["paths"]![path]?[method] is not { } operation)
            {
                continue;
            }

            described++;
            var anonymous = endpoint.Metadata
                .GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>()
                is not null;
            Assert.True(
                anonymous == operation["security"] is null,
                $"{method} {path}: anonymous is {anonymous}.");
        }

        Assert.True(described > 20, $"Only {described} described.");
    }

    /// <summary>Every endpoint of the API, as the app maps it.
    /// </summary>
    private List<RouteEndpoint> ApiEndpoints()
    {
        return _api.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(
                e => e.RoutePattern.RawText!.StartsWith(
                    ApiRoutes.Prefix + "/",
                    StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>The schema type and format a route constraint or a
    /// declared query parameter type stands for.</summary>
    private static (string Type, string? Format) Expected(string kind)
    {
        return kind switch
        {
            "guid" or nameof(Guid) => ("string", "uuid"),
            "long" or nameof(Int64) => ("integer", "int64"),
            nameof(Boolean) => ("boolean", null),
            _ => ("string", null),
        };
    }

    private static string Sample(Type type)
    {
        if (type.IsEnum)
        {
            return Query.Names(type).First();
        }

        return type == typeof(bool) ? "true"
            : type == typeof(Guid) ? Guid.NewGuid().ToString()
            : type == typeof(long) ? "1"
            : throw new InvalidOperationException(type.Name);
    }

    /// <summary>A path for the route, with made-up values that fit
    /// its constraints.</summary>
    private static string PathFor(RouteEndpoint endpoint)
    {
        var path = endpoint.RoutePattern.RawText!;
        foreach (var parameter in endpoint.RoutePattern.Parameters)
        {
            var kind = parameter.ParameterPolicies.Single().Content;
            path = path.Replace(
                $"{{{parameter.Name}:{kind}}}",
                kind == "long" ? "1" : Guid.NewGuid().ToString(),
                StringComparison.Ordinal);
        }

        return path;
    }

    // The expectations come from the app: the parameters in each
    // route, and the query parameters each read declares, which are
    // also the ones its parser accepts.
    [Fact]
    public async Task Document_DescribesEveryRouteAndQueryParameter()
    {
        var document = await DocumentAsync();
        foreach (var endpoint in ApiEndpoints())
        {
            var path = DocumentPath(endpoint.RoutePattern.RawText!);
            var method = endpoint.Metadata
                .GetMetadata<HttpMethodMetadata>()!
                .HttpMethods
                .Single();
            var operation = document["paths"]![path]![
                method.ToLowerInvariant()]!;
            var where = $"{method} {path}";
            var parameters = operation["parameters"]?.AsArray()
                .Select(p => p!)
                .ToList() ?? [];

            foreach (var route in endpoint.RoutePattern.Parameters)
            {
                var documented = parameters.SingleOrDefault(
                    p => p["name"]!.GetValue<string>() == route.Name
                        && p["in"]!.GetValue<string>() == "path");
                Assert.True(
                    documented is not null,
                    $"{where}: {route.Name} is not a path parameter.");
                Assert.True(
                    documented["required"]!.GetValue<bool>(),
                    $"{where}: {route.Name} is not required.");
                var (type, format) = Expected(
                    route.ParameterPolicies.Single().Content!);
                Assert.Equal(
                    type,
                    documented["schema"]!["type"]!.GetValue<string>());
                Assert.Equal(
                    format,
                    documented["schema"]!["format"]?.GetValue<string>());
            }

            Assert.DoesNotContain(
                parameters,
                p => p["in"]!.GetValue<string>() == "path"
                    && endpoint.RoutePattern.GetParameter(
                        p["name"]!.GetValue<string>()) is null);

            var query = parameters
                .Where(p => p["in"]!.GetValue<string>() == "query")
                .ToDictionary(p => p["name"]!.GetValue<string>());
            if (method != "GET")
            {
                Assert.Equal(["dryRun"], query.Keys);
                continue;
            }

            var declared = endpoint.Metadata
                .GetMetadata<QueryParameters>();
            Assert.True(
                declared is not null,
                $"{where} does not declare its query parameters.");
            Assert.Equal(
                declared.All.Select(p => p.Name).Order(StringComparer.Ordinal),
                query.Keys.Order(StringComparer.Ordinal));
            Assert.True(
                operation["responses"]!.AsObject().ContainsKey("400"),
                $"{where} does not document 400.");

            foreach (var parameter in declared.All)
            {
                var schema = query[parameter.Name]["schema"]!;
                Assert.False(
                    string.IsNullOrEmpty(
                        query[parameter.Name]["description"]
                            ?.GetValue<string>()),
                    $"{where}: {parameter.Name} has no description.");
                if (parameter.Type.IsEnum)
                {
                    Assert.Equal(
                        Query.Names(parameter.Type),
                        schema["enum"]!.AsArray()
                            .Select(v => v!.GetValue<string>()));
                }
                else
                {
                    var (type, format) = Expected(parameter.Type.Name);
                    Assert.Equal(type, schema["type"]!.GetValue<string>());
                    Assert.Equal(format, schema["format"]?.GetValue<string>());
                }

                // Each declared one is taken...
                var taken = await _api.SendAsync(
                    "GET",
                    $"{PathFor(endpoint)}?{parameter.Name}="
                    + Sample(parameter.Type));
                Assert.True(
                    taken.Status != HttpStatusCode.BadRequest,
                    $"{where}?{parameter.Name}: {taken.Body}");
            }

            // ...and nothing else is.
            var refused = await _api.SendAsync(
                "GET",
                $"{PathFor(endpoint)}?unexpected=1");
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.NotEmpty(refused.Error("unexpected"));
        }
    }

    [Fact]
    public async Task Document_DescribesInstantsAsText()
    {
        var document = await DocumentAsync();
        var schemas = document["components"]!["schemas"]!;

        var at = schemas["ChangeView"]!["properties"]!["at"]!;
        var completedAt = schemas["ItemView"]!["properties"]!["completedAt"]!;

        Assert.Equal("date-time", at["format"]!.GetValue<string>());
        Assert.Equal("string", at["type"]!.GetValue<string>());
        Assert.Equal("date-time", completedAt["format"]!.GetValue<string>());
        Assert.Contains("string", completedAt["type"]!.ToJsonString());
    }

    // The documented fields of each PATCH body are exactly the ones
    // the endpoint takes: each is sent, alone, and must be applied.
    // Anything else is refused.
    [Fact]
    public async Task Patch_TakesEveryDocumentedProperty()
    {
        var document = await DocumentAsync();
        var patches = document["paths"]!
            .AsObject()
            .Where(path => path.Value!["patch"] is not null)
            .ToList();
        Assert.Equal(
            PatchValues.Keys.Order(StringComparer.Ordinal),
            patches.Select(p => p.Key).Order(StringComparer.Ordinal));

        foreach (var (path, item) in patches)
        {
            var schema = Resolve(
                document,
                item!["patch"]!["requestBody"]!["content"]![
                    "application/json"]!["schema"]!);
            var documented = schema["properties"]!
                .AsObject()
                .Select(p => p.Key)
                .Order(StringComparer.Ordinal)
                .ToList();
            Assert.Equal(
                PatchValues[path].Keys.Order(StringComparer.Ordinal),
                documented);

            foreach (var property in documented)
            {
                var target = await TargetAsync(path);
                var value = PatchValues[path][property];
                var reply = await _api.SendAsync(
                    "PATCH",
                    target,
                    $$"""{"{{property}}":{{value}}}""");

                Assert.True(
                    reply.Status == HttpStatusCode.OK,
                    $"{path} {property}: {reply.Body}");
                Assert.NotNull(reply.Json["change"]);
                Assert.Equal(
                    JsonNode.Parse(value)?.ToJsonString() ?? "null",
                    reply.Json["data"]![property]?.ToJsonString() ?? "null");
            }

            var unknown = await _api.SendAsync(
                "PATCH",
                await TargetAsync(path),
                """{"colour":"red"}""");
            Assert.Equal(HttpStatusCode.BadRequest, unknown.Status);
            Assert.StartsWith("Unknown property.", unknown.Error("colour"));
        }
    }

    /// <summary>Something to PATCH at a path such as
    /// "/api/items/{id}", which has values that every sample value
    /// changes.</summary>
    private async Task<string> TargetAsync(string path)
    {
        switch (path)
        {
            case "/api/items/{id}":
                var project = await _api.CreateProjectAsync("House");
                var item = await _api.CreateItemAsync(
                    $$"""{"title":"Water","projectId":"{{project}}"}""");
                return $"/api/items/{item["id"]}";
            case "/api/projects/{id}":
                var house = await _api.CreateProjectAsync("House");
                var shed = await _api.CreateProjectAsync("Shed", house);
                return $"/api/projects/{shed}";
            case "/api/contexts/{id}":
                var phone = await _api.CreateContextAsync(
                    $"phone {Guid.NewGuid()}");
                return $"/api/contexts/{phone}";
            default:
                throw new InvalidOperationException(path);
        }
    }
}
