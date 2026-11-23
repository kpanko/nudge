using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nudge.Core;
using Nudge.Web.Auth;
using Nudge.Web.Data;
using Nudge.Web.Tests;
using Nudge.Web.Tests.Ui;

namespace Nudge.Web.UiTests;

/// <summary>
/// The real app served by Kestrel on a free port of the loopback
/// address, on a database of its own with a little invented
/// data, signed in to through <see cref="FakeGoogle"/>. The fake
/// Google's sign-in page is served by the test, on the app's host
/// at <see cref="SignInPagePath"/> (a branch added by a startup
/// filter, before anything of the app's): it sends the browser
/// straight back to the app with a code, as Google does once the
/// owner has chosen their account. (A Playwright route cannot stand
/// in for it: routes do not see a request reached by a redirect.)
/// </summary>
internal sealed class BrowserHost : IAsyncDisposable
{
    public const string OwnerEmail = "owner@example.test";
    public const string OwnerSubject = "100000000000000000001";

    /// <summary>Where the test serves the fake Google's sign-in
    /// page.</summary>
    public const string SignInPagePath = "/test-only/fake-google/auth";

    private readonly WebApplicationFactory<Program> _app;
    private readonly X509Certificate2 _certificate = Certificate();

    private BrowserHost()
    {
        Factory = new NudgeWebFactory();
        _app = Factory.WithWebHostBuilder(
            builder =>
            {
                builder.UseSetting(
                    OwnerSignIn.ClientIdSetting,
                    FakeGoogle.ClientId);
                builder.UseSetting(
                    OwnerSignIn.ClientSecretSetting,
                    "not-a-real-secret");
                builder.UseSetting(OwnerSignIn.EmailSetting, OwnerEmail);
                builder.ConfigureLogging(
                    logging => logging.AddProvider(Log));
                builder.ConfigureTestServices(
                    services =>
                    {
                        services.Configure<OpenIdConnectOptions>(
                            OwnerAuthentication.GoogleScheme,
                            options =>
                            {
                                Google.Configure(options);
                                options.Configuration!.AuthorizationEndpoint =
                                    BaseAddress + SignInPagePath;
                            });
                        services.AddSingleton<IStartupFilter>(
                            new SignInPage(this));
                    });
            });
        // Port 0: the system picks a free port as Kestrel binds, and
        // the address is read back once it has started, so no other
        // process can take the port in between.
        _app.UseKestrel(
            options => options.Listen(
                IPAddress.Loopback,
                0,
                listen => listen.UseHttps(_certificate)));
    }

    /// <summary>Where the browser reaches the app: HTTPS, as in
    /// production, with a certificate made for the run that the
    /// browser is told to accept. (Over plain HTTP the app's
    /// antiforgery, set to Secure cookies only, refuses to work.)
    /// </summary>
    public string BaseAddress { get; private set; } = "";

    public FakeGoogle Google { get; } = new();

    /// <summary>The app's warnings and errors.</summary>
    public ServerLog Log { get; } = new();

    public NudgeWebFactory Factory { get; }

    /// <summary>The running app's services, to seed a test's data.
    /// </summary>
    public IServiceProvider Services => _app.Services;

    public static async Task<BrowserHost> StartAsync()
    {
        var host = new BrowserHost();
        host._app.StartServer();
        host.BaseAddress = host._app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .Single()
            .TrimEnd('/');
        await host.SeedAsync();
        return host;
    }

    /// <summary>The fake Google's sign-in page: a form that posts
    /// a code for the owner back to the app at once.</summary>
    private async Task SignInPageAsync(HttpContext http)
    {
        var query = http.Request.Query;
        var code = Google.Issue(
            OwnerEmail,
            true,
            OwnerSubject,
            query["nonce"].ToString());
        var action = WebUtility.HtmlEncode(query["redirect_uri"].ToString());
        var state = WebUtility.HtmlEncode(query["state"].ToString());
        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.WriteAsync(
            $"""
            <!DOCTYPE html>
            <html><head><link rel="icon" href="data:,"></head><body>
            <form method="post" action="{action}">
              <input type="hidden" name="code" value="{code}">
              <input type="hidden" name="state" value="{state}">
            </form>
            <script>document.forms[0].submit();</script>
            </body></html>
            """);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        Factory.Dispose();
        _certificate.Dispose();
    }

    /// <summary>Puts the fake Google's page in front of the app.
    /// </summary>
    private sealed class SignInPage(BrowserHost host) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(
            Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Map(
                    SignInPagePath,
                    page => page.Run(host.SignInPageAsync));
                next(app);
            };
        }
    }

    /// <summary>A self-signed certificate for localhost, valid for
    /// a day either side of now.</summary>
    private static X509Certificate2 Certificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")],
                critical: false));
        using var made = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        // Loaded again from PKCS #12, so that Kestrel on Windows can
        // use the private key.
        return X509CertificateLoader.LoadPkcs12(
            made.Export(X509ContentType.Pkcs12),
            null);
    }

    /// <summary>Invented items for the lists only looked over.
    /// </summary>
    private async Task SeedAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NudgeDbContext>();
        db.Source = "seed";
        db.Description = "Seed for the smoke test";
        db.Items.Add(
            new Item("Hear back from the landlord")
            {
                State = ItemState.WaitingFor,
            });
        db.Items.Add(new Item("Learn to sail") { State = ItemState.Someday });
        db.Projects.Add(new Project("House"));
        await db.SaveChangesAsync();
    }
}
