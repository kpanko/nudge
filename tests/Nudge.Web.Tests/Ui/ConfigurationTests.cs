using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nudge.Web.Auth;
using Nudge.Web.Components;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// What the configuration does to the web UI: without sign-in
/// configured every page stays locked and the API works; a cookie is
/// checked against the configuration of the app that reads it, so a
/// change of owner shuts it out; and the keys that encrypt cookies
/// are kept where <see cref="WebUi.KeysPathSetting"/> says.
/// </summary>
public sealed class ConfigurationTests : IDisposable
{
    private const string Cookie = "__Host-nudge";

    private readonly string _keys = Path.Combine(
        Path.GetTempPath(),
        $"nudge-keys-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_keys))
        {
            Directory.Delete(_keys, recursive: true);
        }
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

    /// <summary>An app sharing the keys folder, with these settings
    /// on top of the configured sign-in.</summary>
    private WebUiHost Host(
        bool configured = true,
        params (string Key, string? Value)[] settings)
    {
        var all = settings.ToDictionary(s => s.Key, s => s.Value);
        all[WebUi.KeysPathSetting] = _keys;
        return new WebUiHost(all, configured);
    }

    /// <summary>The owner's cookie, made by an app sharing the keys
    /// folder.</summary>
    private async Task<string> OwnersCookieAsync()
    {
        using var first = Host();
        await first.SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/")).StatusCode);
        return first.Cookies[Cookie];
    }

    private static Task<HttpResponseMessage> HomeWithAsync(
        WebUiHost host,
        string cookie)
    {
        host.Cookies[Cookie] = cookie;
        return host.GetAsync("/");
    }

    [Fact]
    public async Task KeysPath_KeepsTheKeys_SoACookieOutlivesARestart()
    {
        var cookie = await OwnersCookieAsync();

        Assert.NotEmpty(Directory.GetFiles(_keys, "*.xml"));
        using var restarted = Host();
        Assert.Equal(
            HttpStatusCode.OK,
            (await HomeWithAsync(restarted, cookie)).StatusCode);
    }

    [Fact]
    public async Task AnotherOwner_ShutsOutTheCookieMadeBefore()
    {
        var cookie = await OwnersCookieAsync();

        using var changed = Host(
            settings: (OwnerSignIn.EmailSetting, "new-owner@example.test"));
        var reply = await HomeWithAsync(changed, cookie);

        AssertSentToSignIn(reply, "another owner");
        Assert.False(changed.Cookies.ContainsKey(Cookie));
    }

    // The configuration is read again on every request, so a change
    // of owner in a running app takes effect without a restart.
    [Fact]
    public async Task OwnerChangedWhileRunning_ShutsOutTheCookie()
    {
        using var host = new WebUiHost();
        await host.SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/")).StatusCode);

        host.Services
            .GetRequiredService<Microsoft.Extensions.Configuration
                .IConfiguration>()[OwnerSignIn.EmailSetting] =
            "new-owner@example.test";

        AssertSentToSignIn(await host.GetAsync("/"), "owner changed");
    }

    [Fact]
    public async Task PinnedSubject_ShutsOutAnotherSubjectsCookie()
    {
        var cookie = await OwnersCookieAsync();

        using var pinned = Host(
            settings: (OwnerSignIn.SubjectSetting, "100000000000000000002"));

        AssertSentToSignIn(await HomeWithAsync(pinned, cookie), "pinned");
    }

    [Fact]
    public async Task PinnedSubject_IsRequiredAtSignIn()
    {
        using var pinned = Host(
            settings: (OwnerSignIn.SubjectSetting, WebUiHost.OwnerSubject));

        await pinned.SignInAsync(subject: "100000000000000000002");
        Assert.False(pinned.Cookies.ContainsKey(Cookie));

        await pinned.SignInAsync();
        Assert.True(pinned.Cookies.ContainsKey(Cookie));
        Assert.Equal(
            HttpStatusCode.OK,
            (await pinned.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task SignInNoLongerConfigured_ShutsOutTheCookie()
    {
        var cookie = await OwnersCookieAsync();

        using var unconfigured = Host(configured: false);

        AssertSentToSignIn(
            await HomeWithAsync(unconfigured, cookie),
            "not configured");
    }

    public static IEnumerable<object[]> Missing =>
        new[]
        {
            OwnerSignIn.ClientIdSetting,
            OwnerSignIn.ClientSecretSetting,
            OwnerSignIn.EmailSetting,
        }.Select(setting => new object[] { setting });

    // Any one of the three missing is the same as none set.
    [Theory]
    [MemberData(nameof(Missing))]
    public async Task OneSettingMissing_LocksTheUi(string setting)
    {
        using var host = Host(settings: (setting, " "));

        var signIn = await host.GetAsync("/signin");

        Assert.Equal(
            OwnerAuthentication.UnavailablePath,
            signIn.Headers.Location!.ToString());
    }

    [Fact]
    public async Task NotConfigured_NoPageIsReachable_AndTheApiWorks()
    {
        using var host = new WebUiHost(configured: false);
        var schemes = host.Services
            .GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Null(
            await schemes.GetSchemeAsync(OwnerAuthentication.GoogleScheme));

        var pages = host.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => !EndpointSides.IsApiSide(e))
            .Where(
                e => e.Metadata
                    .GetMetadata<Microsoft.AspNetCore.Authorization
                        .IAllowAnonymous>() is null)
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Where(p => !p.Contains('{', StringComparison.Ordinal))
            .Distinct()
            .ToList();
        Assert.Contains("/", pages);
        foreach (var page in pages.Append("/nothing-here"))
        {
            AssertSentToSignIn(await host.GetAsync(page), page);
        }

        var signIn = await host.GetAsync("/signin?returnUrl=%2F");
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal(
            OwnerAuthentication.UnavailablePath,
            signIn.Headers.Location!.ToString());
        var unavailable = await host.GetAsync(
            OwnerAuthentication.UnavailablePath);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Contains(
            "Sign-in is not configured",
            await unavailable.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // Google's callback is not there to be called: no handler
        // takes it, and the fallback policy refuses it.
        var callback = await host.SendAsync(
            HttpMethod.Post,
            OwnerAuthentication.CallbackPath,
            new FormUrlEncodedContent(
                new Dictionary<string, string> { ["code"] = "x" }));
        Assert.Equal(HttpStatusCode.Unauthorized, callback.StatusCode);
        Assert.DoesNotContain(Cookie, host.Cookies.Keys);

        Assert.Contains(
            host.Logs.Entries,
            e => e.Level == LogLevel.Warning
                && e.Message.Contains(
                    "Sign-in to the web UI is not configured",
                    StringComparison.Ordinal));
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.GetAsync("/healthz")).StatusCode);
        var token = await host.CreateTokenAsync("tests");
        var read = await host.SendAsync(
            HttpMethod.Get,
            "/api/items",
            authorization: $"Bearer {token}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public void Configured_LogsNoWarning()
    {
        using var host = new WebUiHost();
        _ = host.Services;

        Assert.DoesNotContain(
            host.Logs.Entries,
            e => e.Message.Contains(
                "not configured",
                StringComparison.Ordinal));
    }
}
