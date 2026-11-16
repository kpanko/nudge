using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Nudge.Web.Auth;

/// <summary>
/// The web UI's sign-in: Google (OpenID Connect) checks who it is,
/// <see cref="OwnerSignIn.Allows"/> decides whether that is the
/// owner, and a cookie keeps the owner signed in. This is a scheme
/// apart from the API's: the cookie is never read on the API's
/// endpoints, which keep the API token as the only way in, and an
/// API token never opens a page of the web UI.
/// </summary>
public static class OwnerAuthentication
{
    /// <summary>The cookie that keeps the owner signed in.</summary>
    public const string CookieScheme = "OwnerCookie";

    /// <summary>Signing in with Google. Only registered when
    /// sign-in is configured.</summary>
    public const string GoogleScheme = "Google";

    /// <summary>The policy every page of the web UI needs.</summary>
    public const string Policy = "Owner";

    /// <summary>What the owner's identity is marked with, so that it
    /// cannot be mistaken for any other.</summary>
    public const string IdentityType = "Owner";

    public const string SubjectClaim = "sub";
    public const string EmailClaim = "email";

    public const string SignInPath = "/signin";
    public const string SignOutPath = "/signout";
    public const string CallbackPath = "/signin-google";
    public const string AccessDeniedPath = "/access-denied";
    public const string UnavailablePath = "/sign-in-unavailable";
    public const string SignedOutPath = "/signed-out";

    private const string GoogleAuthority = "https://accounts.google.com";

    /// <summary>The issuers Google's id tokens may name.</summary>
    public static readonly IReadOnlyList<string> GoogleIssuers =
        [GoogleAuthority, "accounts.google.com"];

    /// <summary>
    /// Registers the cookie, Google when it is configured, and the
    /// <see cref="Policy"/>. The API's scheme stays the default and
    /// the fallback policy stays the API token's, so an endpoint
    /// that does not ask for this policy never reads the cookie.
    /// </summary>
    public static IServiceCollection AddOwnerAuthentication(
        this IServiceCollection services,
        OwnerSignIn signIn)
    {
        services.AddSingleton(signIn);
        var authentication = services.AddAuthentication();
        authentication.AddCookie(
            CookieScheme,
            options =>
            {
                options.Cookie.Name = "__Host-nudge";
                options.Cookie.Path = "/";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

                // Strict would drop the cookie on the redirect back
                // from Google; the sign-out form carries an
                // antiforgery token instead.
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.LoginPath = SignInPath;
                options.AccessDeniedPath = AccessDeniedPath;
                options.ReturnUrlParameter = "returnUrl";
                options.Events.OnValidatePrincipal = context =>
                    ValidateCookieAsync(context, signIn);
            });
        if (signIn.Configured)
        {
            authentication.AddOpenIdConnect(
                GoogleScheme,
                options => Google(options, signIn));
        }

        services.AddAuthorization(
            options => options.AddPolicy(
                Policy,
                policy => policy
                    .AddAuthenticationSchemes(CookieScheme)
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context => IsOwner(context.User))));
        return services;
    }

    /// <summary>Whether the user is the signed-in owner.</summary>
    public static bool IsOwner(ClaimsPrincipal user)
    {
        return user.Identities.Any(
            identity => identity.IsAuthenticated
                && identity.AuthenticationType == IdentityType);
    }

    /// <summary>
    /// <c>GET /signin</c> starts the sign-in with Google, or says it
    /// is not configured; <c>POST /signout</c> signs out, with an
    /// antiforgery token. Google's answer comes back to
    /// <see cref="CallbackPath"/>, which the Google scheme handles
    /// itself.
    /// </summary>
    public static void MapOwnerSignIn(this IEndpointRouteBuilder app)
    {
        app.MapGet(SignInPath, SignInAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();
        app.MapPost(SignOutPath, SignOutAsync)
            .RequireAuthorization(Policy)
            .ExcludeFromDescription();
    }

    /// <summary>
    /// Where to go after signing in: the address asked for if it is
    /// a path on this site, and the home page otherwise, so that the
    /// sign-in cannot be used to send someone elsewhere.
    /// </summary>
    public static string LocalReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)
            || url[0] != '/'
            || (url.Length > 1 && url[1] is '/' or '\\')
            || url.Any(c => char.IsControl(c) || c == '\\'))
        {
            return "/";
        }

        return url;
    }

    private static async Task<IResult> SignInAsync(
        HttpContext http,
        OwnerSignIn signIn,
        string? returnUrl)
    {
        if (!signIn.Configured)
        {
            return Results.Redirect(UnavailablePath);
        }

        var target = LocalReturnUrl(returnUrl);
        var current = await http.AuthenticateAsync(CookieScheme);
        if (current.Principal is { } user && IsOwner(user))
        {
            return Results.LocalRedirect(target);
        }

        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = target },
            [GoogleScheme]);
    }

    private static async Task<IResult> SignOutAsync(
        HttpContext http,
        IAntiforgery antiforgery)
    {
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return Results.Text(
                "The sign-out form has expired. Go back, reload the"
                + " page and try again.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        await http.SignOutAsync(CookieScheme);
        return Results.Redirect(SignedOutPath);
    }

    private static void Google(
        OpenIdConnectOptions options,
        OwnerSignIn signIn)
    {
        options.Authority = GoogleAuthority;
        options.ClientId = signIn.ClientId;
        options.ClientSecret = signIn.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("email");
        options.CallbackPath = CallbackPath;
        options.SignInScheme = CookieScheme;

        // Claims keep the names Google gives them (sub, email,
        // email_verified), and nothing but the id token is used.
        options.MapInboundClaims = false;

        // Google documents its id tokens' iss as either of these;
        // any other issuer is refused.
        options.TokenValidationParameters.ValidIssuers = GoogleIssuers;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.SaveTokens = false;
        options.Events.OnTokenValidated = context =>
        {
            var principal = context.Principal;
            var email = principal?.FindFirstValue(EmailClaim);
            var verified = string.Equals(
                principal?.FindFirstValue("email_verified"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            var subject = principal?.FindFirstValue(SubjectClaim);
            if (!signIn.Allows(email, verified, subject))
            {
                // The address is not logged: it is someone's personal
                // data, and the refusal is all that matters.
                Logger(context.HttpContext).LogWarning(
                    "A Google account that is not the owner's was"
                    + " refused.");
                context.Fail("The account is not the owner's.");
                return Task.CompletedTask;
            }

            context.Principal = Owner(subject!, email!);
            context.Properties!.IsPersistent = true;
            return Task.CompletedTask;
        };
        options.Events.OnRemoteFailure = context =>
        {
            // A refused account, a cancelled sign-in, or an answer
            // that does not check out: no cookie is set, and the
            // owner is told the sign-in did not work.
            context.Response.Redirect(AccessDeniedPath);
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }

    /// <summary>The owner as the cookie keeps them: only the Google
    /// id and the address, under <see cref="IdentityType"/>.</summary>
    private static ClaimsPrincipal Owner(
        string subject,
        string email)
    {
        return new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(SubjectClaim, subject),
                    new Claim(EmailClaim, email),
                ],
                IdentityType,
                EmailClaim,
                null));
    }

    /// <summary>
    /// Checks the cookie against the configuration on every request,
    /// so that an owner changed in the configuration, or sign-in no
    /// longer configured, shuts out a cookie made before.
    /// </summary>
    private static async Task ValidateCookieAsync(
        CookieValidatePrincipalContext context,
        OwnerSignIn signIn)
    {
        var principal = context.Principal;
        if (principal is not null
            && IsOwner(principal)
            && signIn.Allows(
                principal.FindFirstValue(EmailClaim),
                true,
                principal.FindFirstValue(SubjectClaim)))
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieScheme);
    }

    private static ILogger Logger(HttpContext http)
    {
        return http.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(OwnerAuthentication).FullName!);
    }
}
