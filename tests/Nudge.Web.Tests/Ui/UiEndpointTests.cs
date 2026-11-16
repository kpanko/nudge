using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Nudge.Web.Auth;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// The web UI's endpoints, the way <c>AuthTests</c> checks the API's:
/// every one needs the owner signed in unless it is on a short list,
/// and the two ways in stay apart. The owner's cookie opens no
/// endpoint of the API, and an API token opens no page.
/// </summary>
public sealed class UiEndpointTests : IDisposable
{
    /// <summary>The web UI's endpoints anyone may reach, besides the
    /// stylesheet and Blazor's script.</summary>
    private static readonly string[] Anonymous =
    [
        "GET /access-denied",
        "GET /sign-in-unavailable",
        "GET /signed-out",
        "GET /signin",
        "POST /access-denied",
        "POST /sign-in-unavailable",
        "POST /signed-out",
    ];

    /// <summary>The route the static files add for a file that is
    /// not there. It does not ask for the owner, and is left to the
    /// fallback policy, which asks for an API token.</summary>
    private const string MissingFile = "{**path:file}";

    private readonly WebUiHost _host = new();

    public void Dispose()
    {
        _host.Dispose();
    }

    private sealed record UiEndpoint(
        string Name,
        string Method,
        string Pattern,
        bool Anonymous,
        bool OwnerOnly,
        bool StaticFile);

    private List<UiEndpoint> Endpoints()
    {
        return _host.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => !EndpointSides.IsApiSide(endpoint))
            .SelectMany(
                endpoint => (endpoint.Metadata
                        .GetMetadata<HttpMethodMetadata>()
                        ?.HttpMethods ?? ["*"])
                    .Select(
                        method => new UiEndpoint(
                            $"{method} {endpoint.RoutePattern.RawText}",
                            method,
                            endpoint.RoutePattern.RawText!,
                            endpoint.Metadata.GetMetadata<IAllowAnonymous>()
                                is not null,
                            endpoint.Metadata
                                .GetOrderedMetadata<IAuthorizeData>()
                                .Any(
                                    data => data.Policy
                                        == OwnerAuthentication.Policy),
                            endpoint.Metadata
                                .GetMetadata<StaticAssetDescriptor>()
                                is not null)))
            .ToList();
    }

    private static void AssertSentToSignIn(
        HttpResponseMessage response,
        string what)
    {
        Assert.True(
            response.StatusCode == HttpStatusCode.Redirect,
            $"{what}: {(int)response.StatusCode}");
        Assert.StartsWith(
            "https://localhost/signin?returnUrl=",
            response.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    private Task<string> TokenAsync()
    {
        return _host.CreateTokenAsync("tests");
    }

    private long Count(string table)
    {
        using var connection = new SqliteConnection(
            $"Data Source={_host.Factory.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return (long)command.ExecuteScalar()!;
    }

    // If this fails, a page or a file of the web UI was opened to
    // anyone, or one was mapped that asks for neither the owner nor
    // anything else.
    [Fact]
    public void EveryUiEndpoint_NeedsTheOwner_ButAFewListed()
    {
        var endpoints = Endpoints();
        Assert.Contains(endpoints, e => e.Name == "GET /");

        Assert.Equal(
            Anonymous,
            endpoints
                .Where(e => e.Anonymous && !e.StaticFile)
                .Select(e => e.Name)
                .Order(StringComparer.Ordinal));
        foreach (var endpoint in endpoints.Where(e => !e.Anonymous))
        {
            Assert.True(
                endpoint.OwnerOnly || endpoint.Pattern == MissingFile,
                $"{endpoint.Name} does not need the owner.");
        }

        foreach (var endpoint in endpoints.Where(e => e.StaticFile))
        {
            Assert.True(
                endpoint.Method is "GET" or "HEAD",
                $"{endpoint.Name}: a static file with another method.");
        }
    }

    [Fact]
    public async Task EveryOwnerOnlyPage_SendsToSignIn_WithoutTheCookie()
    {
        var token = await TokenAsync();
        var pages = Endpoints()
            .Where(e => e.OwnerOnly && !e.Anonymous && e.Method == "GET")
            .Where(e => !e.Pattern.Contains('{', StringComparison.Ordinal))
            .ToList();
        Assert.Contains(pages, e => e.Pattern == "/");

        foreach (var page in pages)
        {
            AssertSentToSignIn(
                await _host.GetAsync(page.Pattern, withCookies: false),
                page.Name);

            // An API token is not a way in.
            AssertSentToSignIn(
                await _host.SendAsync(
                    HttpMethod.Get,
                    page.Pattern,
                    withCookies: false,
                    authorization: $"Bearer {token}"),
                page.Name + " with a token");
        }
    }

    [Fact]
    public async Task SignOut_WithAnApiToken_SendsToSignIn()
    {
        var token = await TokenAsync();

        AssertSentToSignIn(
            await _host.SendAsync(
                HttpMethod.Post,
                OwnerAuthentication.SignOutPath,
                new FormUrlEncodedContent(new Dictionary<string, string>()),
                withCookies: false,
                authorization: $"Bearer {token}"),
            "sign out");
    }

    // The cookie is never read on the API side, so it cannot be
    // used to write from another site.
    [Fact]
    public async Task OwnersCookie_IsRefusedByTheApi()
    {
        await _host.SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await _host.GetAsync("/")).StatusCode);

        var write = await _host.SendAsync(
            HttpMethod.Post,
            "/api/contexts",
            new StringContent(
                """{"name":"phone"}""",
                Encoding.UTF8,
                "application/json"));
        var read = await _host.GetAsync("/api/items");
        var complete = await _host.SendAsync(
            HttpMethod.Post,
            $"/api/items/{Guid.NewGuid()}/complete");

        foreach (var reply in new[] { write, read, complete })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, reply.StatusCode);
            Assert.Equal("Bearer", reply.Headers.WwwAuthenticate.ToString());
        }

        Assert.Equal(0, Count("Contexts"));
        Assert.Equal(0, Count("Changes"));
    }

    [Fact]
    public async Task UnknownPath_WithoutTheCookie_SendsToSignIn()
    {
        AssertSentToSignIn(
            await _host.GetAsync("/nothing-here", withCookies: false),
            "/nothing-here");

        // Another method than GET is refused by the fallback policy:
        // the static files' route for a missing file makes routing
        // answer it as a method that is not allowed, before the web
        // UI's not-found handling.
        var post = await _host.SendAsync(
            HttpMethod.Post,
            "/nothing/here",
            withCookies: false);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [Fact]
    public async Task UnknownPath_ForTheOwner_IsTheNotFoundPage()
    {
        await _host.SignInAsync();

        var reply = await _host.GetAsync("/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, reply.StatusCode);
        Assert.StartsWith(
            "text/html",
            reply.Content.Headers.ContentType!.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "<h1>Not found</h1>",
            await reply.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    // The API side keeps its own answers, cookie or not.
    [Fact]
    public async Task UnknownApiPath_IsStillTheApisAnswer()
    {
        await _host.SignInAsync();
        var token = await TokenAsync();

        var withCookie = await _host.GetAsync("/api/nothing-here");
        var withToken = await _host.SendAsync(
            HttpMethod.Get,
            "/api/nothing-here",
            withCookies: false,
            authorization: $"Bearer {token}");
        var missingDocument = await _host.GetAsync("/openapi/v2.json");

        Assert.Equal(HttpStatusCode.Unauthorized, withCookie.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, missingDocument.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, withToken.StatusCode);
        Assert.StartsWith(
            "application/problem+json",
            withToken.Content.Headers.ContentType!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StylesheetAndScript_AreServedToAnyone()
    {
        foreach (var path in new[] { "/app.css", "/_framework/blazor.web.js" })
        {
            var reply = await _host.GetAsync(path, withCookies: false);
            Assert.True(
                reply.StatusCode == HttpStatusCode.OK,
                $"{path}: {(int)reply.StatusCode}");
        }
    }

    [Fact]
    public async Task HealthCheck_IsUnchanged()
    {
        var reply = await _host.GetAsync("/healthz", withCookies: false);

        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
    }
}
