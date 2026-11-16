using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Nudge.Core;
using Nudge.Web.Auth;
using Nudge.Web.Data;
using Nudge.Web.Tests;
using Nudge.Web.Tests.Ui;

namespace Nudge.Web.UiTests;

/// <summary>
/// The real app served by Kestrel on a free port of
/// <c>localhost</c>, on a database of its own with a little invented
/// data, signed in to through <see cref="FakeGoogle"/>: the browser's
/// requests to the fake Google are answered by
/// <see cref="RouteGoogleAsync"/>, which sends the browser straight
/// back to the app with a code, as Google does once the owner has
/// chosen their account.
/// </summary>
internal sealed class BrowserHost : IAsyncDisposable
{
    public const string OwnerEmail = "owner@example.test";
    public const string OwnerSubject = "100000000000000000001";

    private readonly WebApplicationFactory<Program> _app;

    private BrowserHost(int port)
    {
        BaseAddress = $"http://localhost:{port}";
        Factory = new NudgeWebFactory();
        _app = Factory.WithWebHostBuilder(
            builder =>
            {
                builder.UseSetting(OwnerSignIn.ClientIdSetting, FakeGoogle.ClientId);
                builder.UseSetting(
                    OwnerSignIn.ClientSecretSetting,
                    "not-a-real-secret");
                builder.UseSetting(OwnerSignIn.EmailSetting, OwnerEmail);
                builder.ConfigureTestServices(
                    services => services.Configure<OpenIdConnectOptions>(
                        OwnerAuthentication.GoogleScheme,
                        Google.Configure));
            });
        _app.UseKestrel(
            options => options.ListenLocalhost(port));
    }

    /// <summary>Where the browser reaches the app; localhost, which
    /// the browser treats as secure, so the app's Secure cookies
    /// are kept over plain HTTP.</summary>
    public string BaseAddress { get; }

    public FakeGoogle Google { get; } = new();

    public NudgeWebFactory Factory { get; }

    public static async Task<BrowserHost> StartAsync()
    {
        var host = new BrowserHost(FreePort());
        host._app.StartServer();
        await host.SeedAsync();
        return host;
    }

    /// <summary>Answers the browser's visit to the fake Google: a
    /// page that posts the code back to the app at once.</summary>
    public Task RouteGoogleAsync(IBrowserContext context)
    {
        return context.RouteAsync(
            FakeGoogle.Issuer + "/**",
            async route =>
            {
                var query = QueryHelpers.ParseQuery(
                    new Uri(route.Request.Url).Query);
                var code = Google.Issue(
                    OwnerEmail,
                    true,
                    OwnerSubject,
                    query["nonce"].ToString());
                var action = WebUtility.HtmlEncode(
                    query["redirect_uri"].ToString());
                var state = WebUtility.HtmlEncode(query["state"].ToString());
                await route.FulfillAsync(
                    new RouteFulfillOptions
                    {
                        Status = 200,
                        ContentType = "text/html",
                        Body = $"""
                            <!DOCTYPE html>
                            <html><body>
                            <form method="post" action="{action}">
                              <input type="hidden" name="code" value="{code}">
                              <input type="hidden" name="state" value="{state}">
                            </form>
                            <script>document.forms[0].submit();</script>
                            </body></html>
                            """,
                    });
            });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        Factory.Dispose();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
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
