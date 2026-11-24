using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Nudge.Web.Auth;

namespace Nudge.Web.Components;

/// <summary>
/// Where the web UI is put together: Blazor rendered on the server
/// (static, no interactivity), the owner's sign-in, and what both
/// need. Every path outside the API's (<see cref="IsUiPath"/>) is
/// the web UI's, and every page needs the owner signed in unless it
/// says otherwise.
/// </summary>
public static class WebUi
{
    /// <summary>Where Data Protection keeps its keys, which encrypt
    /// the sign-in and antiforgery cookies. Unset, the keys live in
    /// memory and a restart signs the owner out.</summary>
    public const string KeysPathSetting = "Nudge:DataProtection:KeysPath";

    /// <summary>The page shown for a path that is not there.</summary>
    public const string NotFoundPath = "/not-found";

    /// <summary>The paths that belong to the API side (the API, the
    /// health check and the OpenAPI document), where nothing of the
    /// web UI is served.</summary>
    private static readonly PathString[] ApiSide =
        ["/api", "/healthz", "/openapi"];

    /// <summary>Whether a path belongs to the web UI: everything
    /// that is not on the API side.</summary>
    public static bool IsUiPath(PathString path)
    {
        return !ApiSide.Any(prefix => path.StartsWithSegments(prefix));
    }

    public static IServiceCollection AddNudgeWebUi(
        this IServiceCollection services,
        IConfiguration configuration,
        OwnerSignIn signIn)
    {
        // Fly's proxy ends TLS and tells the app the request came in
        // over HTTPS with X-Forwarded-Proto; the redirect address
        // sent to Google is built from it. Only that header is read,
        // and from any sender: the app is reachable only through
        // Fly's proxy, which sets it. X-Forwarded-For and -Host are
        // not read, so the host in the redirect address cannot be
        // changed by a request.
        services.Configure<ForwardedHeadersOptions>(
            options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });

        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName("Nudge");
        var keysPath = configuration[KeysPathSetting]?.Trim();
        if (!string.IsNullOrEmpty(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(
                new DirectoryInfo(keysPath));
        }
        else
        {
            // Not the default folder in the user's profile (or
            // ~/.aspnet in a container), which would be read and
            // written otherwise: unset means the keys live in this
            // process only.
            services.Configure<KeyManagementOptions>(
                options => options.XmlRepository = new MemoryKeyRepository());
        }

        services.AddAntiforgery(
            options =>
            {
                options.Cookie.Name = "__Host-nudge-antiforgery";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });
        services.AddRazorComponents();
        services.AddHttpContextAccessor();
        services.AddScoped<UiQueries>();
        services.AddScoped<WebWrites>();
        services.AddOwnerAuthentication(signIn);
        return services;
    }

    /// <summary>Maps the pages, their files and the sign-in.
    /// </summary>
    public static void MapNudgeWebUi(this WebApplication app)
    {
        // The stylesheet and Blazor's script; the sign-in pages use
        // them too, and they are public in the repository anyway.
        app.MapStaticAssets().AllowAnonymous();
        app.MapRazorComponents<App>()
            .RequireAuthorization(OwnerAuthentication.Policy);
        app.MapOwnerSignIn();

        if (!app.Services.GetRequiredService<OwnerSignIn>().Configured)
        {
            app.Logger.LogWarning(
                "Sign-in to the web UI is not configured, so every page"
                + " of it is locked. Set {ClientId}, {ClientSecret} and"
                + " {Email}. The API is not affected.",
                OwnerSignIn.ClientIdSetting,
                OwnerSignIn.ClientSecretSetting,
                OwnerSignIn.EmailSetting);
        }
    }

    /// <summary>
    /// Gives a path of the web UI that routing matched to nothing an
    /// endpoint that needs the owner signed in and answers 404, which
    /// then shows <see cref="NotFoundPath"/>. So an address that is
    /// not there tells someone not signed in nothing more than one
    /// that is. A path on the API side is left to the API's rules.
    /// It goes after routing and before authorization.
    /// </summary>
    /// <remarks>
    /// Not a fallback route: one that matched every path would take
    /// part in matching the API's routes too, and turn its requests
    /// without a content type into 404s.
    /// </remarks>
    public static IApplicationBuilder UseUiNotFound(
        this IApplicationBuilder app)
    {
        return app.Use(
            (context, next) =>
            {
                if (context.GetEndpoint() is null
                    && IsUiPath(context.Request.Path))
                {
                    context.SetEndpoint(NotFound);
                }

                return next(context);
            });
    }

    /// <summary>
    /// Answers a form of the web UI sent again with a key already
    /// carried out, the same way as the first time: back to where
    /// the first went, with its change to undo. Blazor would refuse
    /// it once the first write had taken the form off the page (an
    /// item processed, completed or deleted). Only for a page that
    /// needs the owner, the owner signed in, a valid antiforgery
    /// token, and a key whose record has the same fingerprint (see
    /// <see cref="WebWrites.FormFingerprint"/>); anything else goes
    /// on to the page, which writes through the runner as ever. It
    /// reads the record and writes nothing. It goes after
    /// authorization and antiforgery.
    /// </summary>
    public static IApplicationBuilder UseUiReplay(
        this IApplicationBuilder app)
    {
        return app.Use(
            async (context, next) =>
            {
                if (await ReplayAsync(context) is { } target)
                {
                    context.Response.Redirect(target);
                    return;
                }

                await next(context);
            });
    }

    private static async Task<string?> ReplayAsync(HttpContext context)
    {
        var request = context.Request;
        // The antiforgery result first: the form cannot be read at
        // all when the token is not valid.
        if (!HttpMethods.IsPost(request.Method)
            || !IsUiPath(request.Path)
            || context.GetEndpoint()?.Metadata
                .GetMetadata<Microsoft.AspNetCore.Components.Endpoints
                    .ComponentTypeMetadata>() is null
            || !OwnerAuthentication.IsOwner(context.User)
            || context.Features
                .Get<Microsoft.AspNetCore.Antiforgery
                    .IAntiforgeryValidationFeature>() is not { IsValid: true }
            || !request.HasFormContentType)
        {
            return null;
        }

        var form = await request.ReadFormAsync(context.RequestAborted);
        // The capture is on every page that has it, even one whose
        // item is gone (see QuickAdd.PageIsGone), so the page itself
        // answers its repeat: the runner carries it out once, and the
        // page knows to go to the inbox when it is gone.
        if (form["_handler"] == Shared.QuickAdd.FormName
            || WebWrites.RecordKey(form["key"]) is not { } key)
        {
            return null;
        }

        var record = await context.RequestServices
            .GetRequiredService<Nudge.Web.Data.NudgeDbContext>()
            .IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.Key == key,
                context.RequestAborted);
        return record is not null
            && record.Fingerprint == WebWrites.FormFingerprint(
                request,
                form)
            ? WebWrites.Target(form["then"], record.ChangeId)
            : null;
    }

    private static readonly Endpoint NotFound = new(
        context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        },
        new EndpointMetadataCollection(
            new Microsoft.AspNetCore.Authorization.AuthorizeAttribute(
                OwnerAuthentication.Policy)),
        "Web UI: not found");

    /// <summary>Keeps Data Protection's keys in memory, for when no
    /// folder is configured.</summary>
    internal sealed class MemoryKeyRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = [];
        private readonly Lock _lock = new();

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            lock (_lock)
            {
                return _elements.Select(e => new XElement(e)).ToList();
            }
        }

        public void StoreElement(
            XElement element,
            string friendlyName)
        {
            lock (_lock)
            {
                _elements.Add(new XElement(element));
            }
        }
    }
}
