using System.Net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nudge.Web.Auth;
using Nudge.Web.Components;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// Signing in to the web UI through the real OpenID Connect handler,
/// with <see cref="FakeGoogle"/> in Google's place: the owner gets
/// in, and any other account, or the owner's without a verified
/// address, gets no cookie.
/// </summary>
public sealed class SignInTests : IDisposable
{
    private const string Cookie = "__Host-nudge";

    private readonly WebUiHost _host = new();

    public void Dispose()
    {
        _host.Dispose();
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

    [Fact]
    public async Task Owner_IsSignedIn_AndSeesTheHomePage()
    {
        var callback = await _host.SignInAsync();

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location!.ToString());
        Assert.True(_host.Cookies.ContainsKey(Cookie));
        var home = await _host.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("Sign out", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnersAddress_IsComparedIgnoringCase()
    {
        await _host.SignInAsync(email: "Owner@Example.TEST");

        Assert.True(_host.Cookies.ContainsKey(Cookie));
        Assert.Equal(
            HttpStatusCode.OK,
            (await _host.GetAsync("/")).StatusCode);
    }

    public static IEnumerable<object?[]> Refused =>
        new (string What, string? Email, bool Verified, string Subject)[]
        {
            ("another account", "someone@example.test", true, "2"),
            ("the address unverified", WebUiHost.OwnerEmail, false,
                WebUiHost.OwnerSubject),
            ("no address", null, true, WebUiHost.OwnerSubject),
            ("an address that only contains the owner's",
                "x" + WebUiHost.OwnerEmail, true, WebUiHost.OwnerSubject),
        }.Select(r => new object?[] { r.What, r.Email, r.Verified, r.Subject });

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task AnyoneElse_IsRefused_WithNoCookie(
        string what,
        string? email,
        bool verified,
        string subject)
    {
        var callback = await _host.SignInAsync(email, verified, subject);

        Assert.True(
            callback.StatusCode == HttpStatusCode.Redirect,
            $"{what}: {(int)callback.StatusCode}");
        Assert.Equal(
            OwnerAuthentication.AccessDeniedPath,
            callback.Headers.Location!.ToString());
        Assert.False(_host.Cookies.ContainsKey(Cookie), what);
        AssertSentToSignIn(await _host.GetAsync("/"), what);
        var denied = await _host.GetAsync(OwnerAuthentication.AccessDeniedPath);
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        Assert.Contains(
            _host.Logs.Entries,
            e => e.Level == LogLevel.Warning
                && e.Message.Contains(
                    "not the owner's",
                    StringComparison.Ordinal));
    }

    // The refused address is someone's personal data.
    [Fact]
    public async Task RefusedAddress_IsNotLogged()
    {
        await _host.SignInAsync("someone@example.test");

        Assert.DoesNotContain(
            _host.Logs.Entries,
            e => e.Message.Contains("someone@", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IdToken_ForAnotherClient_IsRefused()
    {
        var challenge = await _host.GetAsync("/signin");
        var query = WebUiHost.Query(challenge.Headers.Location!);
        var code = _host.Google.Issue(
            WebUiHost.OwnerEmail,
            true,
            WebUiHost.OwnerSubject,
            query["nonce"],
            audience: "another-client.apps.example.test");

        var callback = await _host.SendAsync(
            HttpMethod.Post,
            OwnerAuthentication.CallbackPath,
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["state"] = query["state"],
                }));

        Assert.Equal(
            OwnerAuthentication.AccessDeniedPath,
            callback.Headers.Location!.ToString());
        Assert.False(_host.Cookies.ContainsKey(Cookie));
    }

    [Fact]
    public async Task Callback_WithoutASignInStarted_IsRefused()
    {
        var code = _host.Google.Issue(
            WebUiHost.OwnerEmail,
            true,
            WebUiHost.OwnerSubject,
            "made-up-nonce");

        var callback = await _host.SendAsync(
            HttpMethod.Post,
            OwnerAuthentication.CallbackPath,
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["state"] = "made-up-state",
                }));

        Assert.Equal(
            OwnerAuthentication.AccessDeniedPath,
            callback.Headers.Location!.ToString());
        Assert.False(_host.Cookies.ContainsKey(Cookie));
    }

    public static IEnumerable<object[]> ReturnUrls =>
        new (string Asked, string Given)[]
        {
            ("/", "/"),
            ("/not-built-yet?x=1", "/not-built-yet?x=1"),
            ("https://evil.example.test/", "/"),
            ("//evil.example.test/", "/"),
            ("/\\evil.example.test/", "/"),
            ("evil", "/"),
            ("", "/"),
        }.Select(r => new object[] { r.Asked, r.Given });

    [Theory]
    [MemberData(nameof(ReturnUrls))]
    public async Task ReturnUrl_IsFollowedOnlyOnThisSite(
        string asked,
        string given)
    {
        var callback = await _host.SignInAsync(returnUrl: asked);

        Assert.Equal(given, callback.Headers.Location!.ToString());
    }

    [Theory]
    [MemberData(nameof(ReturnUrls))]
    public void LocalReturnUrl_KeepsOnlyPathsOnThisSite(
        string asked,
        string given)
    {
        Assert.Equal(given, OwnerAuthentication.LocalReturnUrl(asked));
    }

    [Fact]
    public async Task Page_WithoutSigningIn_SendsToSignIn_AndBack()
    {
        var page = await _host.GetAsync("/");
        AssertSentToSignIn(page, "/");

        var callback = await _host.SignInAsync(
            returnUrl: WebUiHost.Query(page.Headers.Location!)["returnUrl"]);

        Assert.Equal("/", callback.Headers.Location!.ToString());
    }

    [Fact]
    public async Task SignIn_WhenSignedIn_GoesStraightBack()
    {
        await _host.SignInAsync();

        var again = await _host.GetAsync("/signin?returnUrl=%2Fnot-found");

        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Equal("/not-found", again.Headers.Location!.ToString());
    }

    [Fact]
    public async Task SignOut_WithTheFormsToken_EndsTheSession()
    {
        await _host.SignInAsync();
        var home = await (await _host.GetAsync("/")).Content
            .ReadAsStringAsync();

        var signOut = await _host.SendAsync(
            HttpMethod.Post,
            OwnerAuthentication.SignOutPath,
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] =
                        WebUiHost.AntiforgeryToken(home),
                }));

        Assert.Equal(HttpStatusCode.Redirect, signOut.StatusCode);
        Assert.Equal(
            OwnerAuthentication.SignedOutPath,
            signOut.Headers.Location!.ToString());
        Assert.False(_host.Cookies.ContainsKey(Cookie));
        AssertSentToSignIn(await _host.GetAsync("/"), "after signing out");
        Assert.Equal(
            HttpStatusCode.OK,
            (await _host.GetAsync(OwnerAuthentication.SignedOutPath))
                .StatusCode);
    }

    [Fact]
    public async Task SignOut_WithoutTheFormsToken_IsRefused()
    {
        await _host.SignInAsync();

        var signOut = await _host.SendAsync(
            HttpMethod.Post,
            OwnerAuthentication.SignOutPath,
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, signOut.StatusCode);
        Assert.True(_host.Cookies.ContainsKey(Cookie));
        Assert.Equal(
            HttpStatusCode.OK,
            (await _host.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task SignOut_ByGet_DoesNothing()
    {
        await _host.SignInAsync();

        await _host.GetAsync(OwnerAuthentication.SignOutPath);

        Assert.True(_host.Cookies.ContainsKey(Cookie));
        Assert.Equal(
            HttpStatusCode.OK,
            (await _host.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Cookie_IsHostOnly_Secure_HttpOnly_AndLax()
    {
        var callback = await _host.SignInAsync();

        var header = callback.Headers
            .GetValues("Set-Cookie")
            .Single(h => h.StartsWith(Cookie + "=", StringComparison.Ordinal))
            .ToLowerInvariant();
        Assert.Contains("secure", header, StringComparison.Ordinal);
        Assert.Contains("httponly", header, StringComparison.Ordinal);
        Assert.Contains("samesite=lax", header, StringComparison.Ordinal);
        Assert.Contains("path=/", header, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", header, StringComparison.Ordinal);
    }

    // What the real Google sign-in is set up with. It cannot be
    // tried without Google, so its settings are checked instead.
    [Fact]
    public void GoogleScheme_IsSetUpForGoogle()
    {
        using var host = new WebUiHost(fakeGoogle: false);
        var options = host.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OwnerAuthentication.GoogleScheme);

        Assert.Equal("https://accounts.google.com", options.Authority);
        Assert.Equal(WebUiHost.ClientId, options.ClientId);
        Assert.Equal(WebUiHost.ClientSecret, options.ClientSecret);
        Assert.Equal("code", options.ResponseType);
        Assert.Equal(["openid", "email"], options.Scope);
        Assert.Equal(
            OwnerAuthentication.CallbackPath,
            options.CallbackPath.Value);
        Assert.Equal(
            OwnerAuthentication.CookieScheme,
            options.SignInScheme);
        Assert.True(options.UsePkce);
        Assert.True(options.RequireHttpsMetadata);
        Assert.False(options.SaveTokens);
        Assert.Equal(
            ["https://accounts.google.com", "accounts.google.com"],
            options.TokenValidationParameters.ValidIssuers);
    }

    // Without a keys folder, the keys are this app's alone: kept in
    // memory, not in the default folder in the user's profile, which
    // a second app would share.
    [Fact]
    public async Task DataProtection_WithoutAKeysPath_KeepsKeysInMemoryOnly()
    {
        var configuration = _host.Services
            .GetRequiredService<IConfiguration>();
        Assert.Null(configuration[WebUi.KeysPathSetting]);
        await _host.SignInAsync();

        var repository = _host.Services
            .GetRequiredService<IOptions<KeyManagementOptions>>()
            .Value
            .XmlRepository;
        Assert.NotNull(repository);
        Assert.IsNotType<FileSystemXmlRepository>(repository);
        Assert.NotEmpty(repository.GetAllElements());

        using var other = new WebUiHost();
        other.Cookies[Cookie] = _host.Cookies[Cookie];
        var reply = await other.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, reply.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _host.GetAsync("/")).StatusCode);
    }

    public static IEnumerable<object[]> Issuers =>
        new (string Issuer, bool Accepted)[]
        {
            ("https://accounts.google.com", true),
            ("accounts.google.com", true),
            ("https://evil.example.test", false),
            ("http://accounts.google.com", false),
            ("accounts.google.com.evil.example.test", false),
        }.Select(r => new object[] { r.Issuer, r.Accepted });

    // Google gives its issuer in either of two forms; nothing else
    // passes. (The fake's own issuer is accepted too, as the issuer
    // of the configuration the test puts in Google's place.)
    [Theory]
    [MemberData(nameof(Issuers))]
    public async Task Issuer_IsOneOfGooglesTwoForms(
        string issuer,
        bool accepted)
    {
        var callback = await _host.SignInAsync(issuer: issuer);

        Assert.Equal(
            accepted ? "/" : OwnerAuthentication.AccessDeniedPath,
            callback.Headers.Location!.ToString());
        Assert.Equal(accepted, _host.Cookies.ContainsKey(Cookie));
    }
}
