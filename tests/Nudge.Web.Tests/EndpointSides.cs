using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Nudge.Web.Tests;

/// <summary>
/// Which side of the app an endpoint is on. The API side is the API
/// itself, the health check and the OpenAPI document, held to the
/// API token's rules; everything else is the web UI's, held to the
/// owner's sign-in. Written here independently of the app's own
/// rule, so that a change to that rule shows up in the tests.
/// </summary>
internal static class EndpointSides
{
    private static readonly PathString[] ApiSide =
        ["/api", "/healthz", "/openapi"];

    public static bool IsApiSide(RouteEndpoint endpoint)
    {
        var path = new PathString(
            "/" + endpoint.RoutePattern.RawText!.TrimStart('/'));
        return ApiSide.Any(prefix => path.StartsWithSegments(prefix));
    }
}
