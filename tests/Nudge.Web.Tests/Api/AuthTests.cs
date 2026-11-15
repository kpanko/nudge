using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nudge.Web.Auth;

namespace Nudge.Web.Tests.Api;

/// <summary>
/// Every endpoint but two needs an active API token, sent as a
/// bearer token; every way of failing looks the same; a refused
/// request does nothing; and tokens never show up in the change
/// log, the API's answers or the log.
/// </summary>
public sealed class AuthTests : IDisposable
{
    /// <summary>The endpoints anyone may call, as method and route.
    /// </summary>
    private static readonly string[] Anonymous =
        ["GET /healthz", "GET /openapi/v1.json"];

    private readonly Api _api = new();

    public void Dispose()
    {
        _api.Dispose();
    }

    public static IEnumerable<object[]> Writes => WriteCatalog.Names;

    public static IEnumerable<object[]> Failures =>
        new[]
        {
            "no header",
            "basic",
            "another scheme with the token",
            "bearer alone",
            "bearer and a space",
            "malformed",
            "prefix only",
            "truncated",
            "lengthened",
            "lower case",
            "unknown",
            "the hash",
            "revoked",
        }.Select(name => new object[] { name });

    /// <summary>A token of the right shape that was never made.
    /// </summary>
    private static string Unknown()
    {
        return ApiTokens.Prefix
            + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
    }

    private async Task<string?> HeaderAsync(string failure)
    {
        return failure switch
        {
            "no header" => null,
            "basic" => "Basic "
                + Convert.ToBase64String(
                    Encoding.UTF8.GetBytes("someone:secret")),
            "another scheme with the token" => $"Token {_api.Token}",
            "bearer alone" => "Bearer",
            "bearer and a space" => "Bearer ",
            "malformed" => "Bearer not-a-token",
            "prefix only" => Api.Bearer(ApiTokens.Prefix),
            "truncated" => Api.Bearer(_api.Token[..^1]),
            "lengthened" => Api.Bearer(_api.Token + "A"),
            "lower case" => Api.Bearer(_api.Token.ToLowerInvariant()),
            "unknown" => Api.Bearer(Unknown()),
            "the hash" => Api.Bearer(ApiTokens.Hash(_api.Token)),
            "revoked" => Api.Bearer(await RevokedAsync()),
            _ => throw new ArgumentOutOfRangeException(failure),
        };
    }

    private async Task<string> RevokedAsync()
    {
        var token = await _api.CreateTokenAsync("old-agent");
        await _api.RevokeTokenAsync("old-agent");
        return token;
    }

    /// <summary>The body without its trace id, which differs from
    /// one request to the next.</summary>
    private static string Normalized(Reply reply)
    {
        var body = reply.Json.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
    }

    private static void AssertRefused(
        Reply reply,
        string what)
    {
        Assert.True(
            reply.Status == HttpStatusCode.Unauthorized,
            $"{what}: {(int)reply.Status} {reply.Body}");
        Assert.Equal("Bearer", reply.WwwAuthenticate);
        Assert.StartsWith("application/problem+json", reply.ContentType);
    }

    // Each failure is answered exactly as a request with no header
    // is, so the answer does not tell a caller which it was.
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task EveryFailure_IsTheSame401(string failure)
    {
        var baseline = await _api.SendAsAsync(null, "GET", "/api/items");
        AssertRefused(baseline, "no header");
        Assert.Equal(401, baseline.Json["status"]!.GetValue<int>());

        var reply = await _api.SendAsAsync(
            await HeaderAsync(failure),
            "GET",
            "/api/items");

        AssertRefused(reply, failure);
        Assert.Equal(Normalized(baseline), Normalized(reply));
    }

    // Only the Authorization header is read.
    [Fact]
    public async Task Token_InTheQueryOrACookie_IsRefused()
    {
        AssertRefused(
            await _api.SendAsAsync(
                null,
                "GET",
                $"/api/items?access_token={_api.Token}"),
            "query");

        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/items");
        message.Headers.Add("Cookie", $"access_token={_api.Token}");
        using var response = await _api.Client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidToken_GetsThroughOnAReadAndAWrite()
    {
        var read = await _api.SendAsync("GET", "/api/items");
        Assert.Equal(HttpStatusCode.OK, read.Status);

        var write = await _api.SendAsync(
            "POST",
            "/api/contexts",
            """{"name":"phone"}""");
        Assert.Equal(HttpStatusCode.Created, write.Status);
        Assert.Equal(1, _api.Count("Contexts"));
    }

    [Fact]
    public async Task Bearer_IsReadInAnyCase()
    {
        var reply = await _api.SendAsAsync(
            $"bearer {_api.Token}",
            "GET",
            "/api/items");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
    }

    [Fact]
    public async Task Change_NamesTheTokenItWasMadeWith()
    {
        var agent = await _api.CreateTokenAsync("planning-agent");

        var reply = await _api.SendAsAsync(
            Api.Bearer(agent),
            "POST",
            "/api/contexts",
            """{"name":"phone"}""");
        await _api.CreateContextAsync("errands");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(
            "api:planning-agent",
            reply.Json["change"]!["source"]!.GetValue<string>());
        var changes = await _api.GetAsync("/api/changes");
        Assert.Equal(
            [$"api:{Api.TokenName}", "api:planning-agent"],
            changes.AsArray().Select(c => c!["source"]!.GetValue<string>()));
    }

    /// <summary>Every endpoint the app maps, with a path that fits
    /// its route.</summary>
    private List<(string Name, string Method, string Path, bool Anonymous)>
        Endpoints()
    {
        return _api.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(
                endpoint => (endpoint.Metadata
                        .GetMetadata<HttpMethodMetadata>()
                        ?.HttpMethods ?? ["GET"])
                    .Select(
                        method => (
                            $"{method} {endpoint.RoutePattern.RawText}",
                            method,
                            PathFor(endpoint),
                            endpoint.Metadata.GetMetadata<IAllowAnonymous>()
                                is not null)))
            .ToList();
    }

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

    // If this fails, an endpoint was opened to anyone. Only Fly's
    // health check and the OpenAPI document may be.
    [Fact]
    public async Task OnlyTheHealthCheckAndTheDocument_AreAnonymous()
    {
        var endpoints = Endpoints();

        Assert.Equal(
            Anonymous,
            endpoints
                .Where(e => e.Anonymous)
                .Select(e => e.Name)
                .Order(StringComparer.Ordinal));
        foreach (var endpoint in endpoints.Where(e => e.Anonymous))
        {
            var reply = await _api.SendAsAsync(
                null,
                endpoint.Method,
                endpoint.Path);
            Assert.Equal(HttpStatusCode.OK, reply.Status);
        }
    }

    // The fallback policy at work: nothing has to opt in.
    [Fact]
    public async Task EveryOtherEndpoint_WithoutAToken_Is401()
    {
        var endpoints = Endpoints().Where(e => !e.Anonymous).ToList();
        Assert.True(endpoints.Count > 20, $"Only {endpoints.Count}.");

        foreach (var endpoint in endpoints)
        {
            var body = endpoint.Method == "GET" ? null : "{}";
            AssertRefused(
                await _api.SendAsAsync(
                    null,
                    endpoint.Method,
                    endpoint.Path,
                    body),
                endpoint.Name);
            AssertRefused(
                await _api.SendAsAsync(
                    Api.Bearer(Unknown()),
                    endpoint.Method,
                    endpoint.Path,
                    body),
                endpoint.Name);
        }
    }

    // A path that matches no endpoint does not tell an anonymous
    // caller what exists.
    [Fact]
    public async Task UnknownPath_WithoutAToken_Is401()
    {
        AssertRefused(
            await _api.SendAsAsync(null, "GET", "/api/nothing-here"),
            "unknown path");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _api.SendAsync("GET", "/api/nothing-here")).Status);
    }

    /// <summary>Requests no endpoint maps as sent, and whether each
    /// routes to one of the two anonymous endpoints all the same.
    /// </summary>
    public static IEnumerable<object[]> Unmapped =>
        new (string Method, string Path, bool Anonymous)[]
        {
            ("HEAD", "/api/items", false),
            ("OPTIONS", "/api/items", false),
            ("OPTIONS", "/api/changes/1/undo", false),
            ("TRACE", "/api/items", false),
            ("POST", "/healthz", false),
            ("OPTIONS", "/healthz", false),
            ("HEAD", "/healthz", false),
            ("DELETE", "/healthz", false),
            ("POST", "/openapi/v1.json", false),
            ("PUT", "/openapi/v1.json", false),
            ("DELETE", "/openapi/v1.json", false),
            ("OPTIONS", "/openapi/v1.json", false),
            ("GET", "/api/nothing-here", false),
            ("POST", "/api/nothing-here", false),
            ("GET", "/api", false),
            ("GET", "/api/", false),
            ("GET", "/nothing-here", false),
            ("GET", "/", false),
            ("GET", "/openapi/v2.json", false),
            ("GET", "/openapi/v1.yaml", false),
            ("GET", "/openapi", false),
            ("GET", "/API/ITEMS", false),
            ("GET", "/api/items/", false),
            ("POST", "/API/CONTEXTS", false),
            ("POST", "/api/contexts/", false),
            ("GET", "/HEALTHZ", true),
            ("GET", "/healthz/", true),
            ("GET", "/OPENAPI/V1.JSON", true),
            ("GET", "/openapi/v1.json/", true),
        }.Select(r => new object[] { r.Method, r.Path, r.Anonymous });

    // The fallback policy covers what no endpoint maps: other
    // methods, other paths and other spellings are all 401 without a
    // token, unless routing takes them to one of the two anonymous
    // endpoints anyway. Nothing is written either way.
    [Theory]
    [MemberData(nameof(Unmapped))]
    public async Task UnmappedRequest_WithoutAToken_Is401(
        string method,
        string path,
        bool anonymous)
    {
        var before = _api.Everything();
        var body = method is "POST" or "PUT" ? """{"name":"phone"}""" : null;

        var reply = await _api.SendAsAsync(null, method, path, body);

        if (anonymous)
        {
            Assert.True(
                reply.Status == HttpStatusCode.OK,
                $"{method} {path}: {(int)reply.Status}");
        }
        else
        {
            Assert.True(
                reply.Status == HttpStatusCode.Unauthorized,
                $"{method} {path}: {(int)reply.Status} {reply.Body}");
            if (method != "HEAD")
            {
                AssertRefused(reply, $"{method} {path}");
            }
        }

        Assert.Equal(before, _api.Everything());
    }

    // Authentication comes before the write runner: nothing is read
    // or written, not even an idempotency record.
    [Theory]
    [MemberData(nameof(Writes))]
    public async Task RefusedWrite_ChangesNothing(string name)
    {
        var request = await WriteCatalog.Named(name).Arrange(_api);
        var before = _api.Everything();

        foreach (var header in new[] { null, Api.Bearer(Unknown()) })
        {
            foreach (var query in new[] { "", "?dryRun=true" })
            {
                AssertRefused(
                    await _api.SendAsAsync(
                        header,
                        request.Method,
                        request.Path + query,
                        request.Body,
                        key: "refused-key"),
                    name);
            }
        }

        Assert.Equal(before, _api.Everything());

        // The key was not used up.
        var real = await _api.SendAsync(request, "refused-key");
        Assert.True(
            real.Status is HttpStatusCode.OK or HttpStatusCode.Created,
            real.Body);
        Assert.False(real.Replayed);
    }

    [Fact]
    public async Task RevokedToken_IsRefusedAtOnce_AndNothingBringsItBack()
    {
        var agent = await _api.CreateTokenAsync("agent");
        var created = await _api.SendAsAsync(
            Api.Bearer(agent),
            "POST",
            "/api/contexts",
            """{"name":"phone"}""");
        Assert.Equal(HttpStatusCode.Created, created.Status);

        // Revoked by the command line while the app runs.
        await _api.RevokeTokenAsync("agent");
        var tokens = _api.Rows("ApiTokens");

        AssertRefused(
            await _api.SendAsAsync(Api.Bearer(agent), "GET", "/api/items"),
            "revoked");

        // Undo everything in the log, newest first, then undo the
        // undos: none of it touches the tokens.
        for (var round = 0; round < 2; round++)
        {
            var changes = await _api.GetAsync("/api/changes?limit=200");
            foreach (var change in changes.AsArray())
            {
                await _api.SendAsync(
                    "POST",
                    $"/api/changes/{change!["id"]}/undo");
            }
        }

        Assert.Equal(tokens, _api.Rows("ApiTokens"));
        AssertRefused(
            await _api.SendAsAsync(Api.Bearer(agent), "GET", "/api/items"),
            "revoked, after the undos");
    }

    [Fact]
    public async Task ChangeLog_NeverShowsTokens()
    {
        var agent = await _api.CreateTokenAsync("agent");
        await _api.CreateContextAsync("phone");
        await _api.SendAsAsync(
            Api.Bearer(agent),
            "POST",
            "/api/projects",
            """{"name":"House","parentId":null}""");
        await _api.RevokeTokenAsync("agent");
        await _api.CreateTokenAsync("another");

        // Two writes; making and revoking tokens recorded nothing.
        Assert.Equal(2, _api.Count("Changes"));
        var secrets = new[]
        {
            _api.Token,
            agent,
            ApiTokens.Hash(_api.Token),
            ApiTokens.Hash(agent),
        };
        var bodies = new List<string>
        {
            (await _api.SendAsync("GET", "/api/changes?limit=200")).Body,
        };
        for (var id = 1; id <= 2; id++)
        {
            var change = await _api.SendAsync("GET", $"/api/changes/{id}");
            bodies.Add(change.Body);
        }

        foreach (var body in bodies)
        {
            Assert.DoesNotContain("ApiTokens", body);
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(
                    secret,
                    body,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // Neither the token, its hash, nor the header is logged, at any
    // level, whether the token is good, unknown or revoked.
    [Fact]
    public async Task Tokens_AreNeverLogged()
    {
        using var api = new Api(everyLog: true);
        var revoked = await api.CreateTokenAsync("old-agent");
        await api.RevokeTokenAsync("old-agent");
        var unknown = Unknown();

        foreach (var token in new[] { api.Token, revoked, unknown })
        {
            await api.SendAsAsync(Api.Bearer(token), "GET", "/api/items");
            await api.SendAsAsync(
                Api.Bearer(token),
                "POST",
                "/api/contexts",
                """{"name":"phone"}""");
        }

        await api.SendAsAsync("Bearer not-a-token", "GET", "/api/items");

        // A token in the query string, where it does not belong. The
        // request log would write the URL; only the app's own filter,
        // not its settings file, keeps it out here.
        foreach (var token in new[] { api.Token, unknown })
        {
            await api.SendAsAsync(
                null,
                "GET",
                $"/api/items?access_token={token}");
            await api.SendAsAsync(
                Api.Bearer(api.Token),
                "GET",
                $"/api/items?token={token}");
            await api.SendAsAsync(
                null,
                "GET",
                $"/healthz?access_token={token}");
        }

        var entries = api.Logs.Entries;

        // The settings did turn logging up.
        Assert.Contains(
            entries,
            e => e.Level == LogLevel.Debug
                && e.Category.StartsWith(
                    "Microsoft.AspNetCore.",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            entries,
            e => e.Category == "Microsoft.AspNetCore.Hosting.Diagnostics"
                && e.Level < LogLevel.Warning);
        foreach (var entry in entries)
        {
            var text = $"{entry.Message} {entry.Exception}";
            Assert.DoesNotContain(
                "Bearer",
                text,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ApiTokens.Prefix, text);
            foreach (var token in new[] { api.Token, revoked, unknown })
            {
                Assert.DoesNotContain(
                    ApiTokens.Hash(token),
                    text,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task FallbackPolicy_RequiresAnAuthenticatedUser()
    {
        var policy = await _api.Services
            .GetRequiredService<IAuthorizationPolicyProvider>()
            .GetFallbackPolicyAsync();

        Assert.NotNull(policy);
        Assert.Contains(
            policy.Requirements,
            r => r is Microsoft.AspNetCore.Authorization.Infrastructure
                .DenyAnonymousAuthorizationRequirement);
    }
}
