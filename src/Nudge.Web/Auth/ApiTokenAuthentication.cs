using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Nudge.Web.Data;

namespace Nudge.Web.Auth;

/// <summary>
/// Authentication with API tokens, sent as
/// <c>Authorization: Bearer &lt;token&gt;</c> and nowhere else (not
/// in the query, not in a cookie). Every endpoint requires it unless
/// it opts out with <c>AllowAnonymous</c>.
/// </summary>
public static class ApiTokenAuthentication
{
    public const string Scheme = "ApiToken";

    /// <summary>
    /// Registers the scheme and makes an authenticated user the
    /// fallback policy, so that an endpoint is protected unless it
    /// says otherwise, including one added later and a path that
    /// matches no endpoint at all.
    /// </summary>
    public static IServiceCollection AddNudgeAuthentication(
        this IServiceCollection services)
    {
        services
            .AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiTokenHandler>(
                Scheme,
                _ => { });
        services.AddAuthorization(
            options => options.FallbackPolicy =
                new AuthorizationPolicyBuilder(Scheme)
                    .RequireAuthenticatedUser()
                    .Build());
        return services;
    }

    /// <summary>The name of the token a request was authenticated
    /// with, or null if it was not.</summary>
    public static string? TokenName(ClaimsPrincipal user)
    {
        return user.Identity is { IsAuthenticated: true }
            && user.Identity.AuthenticationType == Scheme
            ? user.FindFirstValue(ClaimTypes.Name)
            : null;
    }
}

/// <summary>
/// Reads the bearer token, hashes it and looks for an active token
/// with that hash. Every way of failing (no header, another scheme,
/// a malformed, unknown or revoked token) is answered the same: 401,
/// <c>WWW-Authenticate: Bearer</c>, and problem details that do not
/// say which it was. Neither the token nor the header is ever logged.
/// </summary>
internal sealed class ApiTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        logger,
        encoder)
{
    private const string BearerScheme = "Bearer";

    protected override async Task<AuthenticateResult>
        HandleAuthenticateAsync()
    {
        var token = Bearer(Request.Headers.Authorization);
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        var db = Context.RequestServices.GetRequiredService<NudgeDbContext>();
        var found = await ApiTokens.FindActiveAsync(
            db,
            token,
            Context.RequestAborted);
        if (found is not { } active)
        {
            // Logged by the framework: say nothing of the token.
            return AuthenticateResult.Fail("No active API token matches.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, active.Id.ToString()),
                new Claim(ClaimTypes.Name, active.Name),
            ],
            ApiTokenAuthentication.Scheme);
        return AuthenticateResult.Success(
            new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                ApiTokenAuthentication.Scheme));
    }

    protected override async Task HandleChallengeAsync(
        AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers[HeaderNames.WWWAuthenticate] = BearerScheme;
        await Results
            .Problem(
                "Send an active API token as Authorization: Bearer"
                + " <token>.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.")
            .ExecuteAsync(Context);
    }

    /// <summary>The token in a bearer header, or null if there is no
    /// such header. A header of another scheme, or more than one,
    /// counts as none.</summary>
    private static string? Bearer(
        Microsoft.Extensions.Primitives.StringValues values)
    {
        if (values.Count != 1 || values[0] is not { } header)
        {
            return null;
        }

        var space = header.IndexOf(' ', StringComparison.Ordinal);
        if (space < 0
            || !header[..space].Equals(
                BearerScheme,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return header[(space + 1)..].Trim();
    }
}
