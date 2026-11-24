using System.Net;
using System.Text.RegularExpressions;
using Nudge.Web.Auth;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// The "Sign in again" and "Try again" links ask Google for its
/// account picker, through the real OpenID Connect handler. An
/// ordinary sign-in does not, and nothing in the address can change
/// what is sent to Google.
/// </summary>
public sealed class AccountPickerTests : IDisposable
{
    private const string Cookie = "__Host-nudge";
    private const string Picker = "/signin?chooseAccount=1";

    /// <summary>What differs between any two sign-ins.</summary>
    private static readonly string[] PerRequest =
        ["state", "nonce", "code_challenge"];

    private readonly WebUiHost _host = new();

    public void Dispose()
    {
        _host.Dispose();
    }

    /// <summary>Every name and value, so that a repeated parameter
    /// is seen.</summary>
    private static List<KeyValuePair<string, string>> Parameters(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(
            FakeGoogle.AuthorizationEndpoint + "?",
            location.ToString(),
            StringComparison.Ordinal);
        return location.Query
            .TrimStart('?')
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .Select(
                pair => new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(pair[0]),
                    Uri.UnescapeDataString(pair[1])))
            .ToList();
    }

    private static string[] Prompts(HttpResponseMessage response)
    {
        return Parameters(response)
            .Where(p => p.Key == "prompt")
            .Select(p => p.Value)
            .ToArray();
    }

    /// <summary>The parameters with those that change every time
    /// and the prompt left out, sorted for comparing.</summary>
    private static string[] Fixed(HttpResponseMessage response)
    {
        return Parameters(response)
            .Where(p => p.Key != "prompt" && !PerRequest.Contains(p.Key))
            .Select(p => $"{p.Key}={p.Value}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<HttpResponseMessage> FinishAsync(
        HttpResponseMessage challenge,
        string email = WebUiHost.OwnerEmail,
        string subject = WebUiHost.OwnerSubject)
    {
        var query = WebUiHost.Query(challenge.Headers.Location!);
        var code = _host.Google.Issue(
            email,
            true,
            subject,
            query["nonce"]);
        return await _host.SendAsync(
            HttpMethod.Post,
            new Uri(query["redirect_uri"]).AbsolutePath,
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["state"] = query["state"],
                }));
    }

    [Theory]
    [InlineData(OwnerAuthentication.SignedOutPath, "Sign in again")]
    [InlineData(OwnerAuthentication.AccessDeniedPath, "Try again")]
    public async Task Link_LeadsToGooglesPicker(
        string page,
        string text)
    {
        var html = await (await _host.GetAsync(page)).Content
            .ReadAsStringAsync();
        var link = Regex.Match(
            html,
            "<a href=\"([^\"]+)\"[^>]*>" + text + "</a>");
        Assert.True(link.Success, html);

        var challenge = await _host.GetAsync(link.Groups[1].Value);

        Assert.Equal(["select_account"], Prompts(challenge));
    }

    [Fact]
    public async Task OrdinarySignIn_SendsNoPrompt()
    {
        var challenge = await _host.GetAsync("/signin?returnUrl=%2F");

        Assert.Empty(Prompts(challenge));
    }

    [Fact]
    public async Task ExpiredOrAbsentCookie_SendsNoPrompt()
    {
        var page = await _host.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);

        var challenge = await _host.GetAsync(
            page.Headers.Location!.PathAndQuery);

        Assert.Empty(Prompts(challenge));
    }

    [Theory]
    [InlineData("prompt=consent")]
    [InlineData("prompt=select_account")]
    [InlineData("prompt=none")]
    [InlineData("chooseAccount=consent")]
    [InlineData("chooseAccount=select_account")]
    [InlineData("chooseAccount=true")]
    [InlineData("chooseAccount=")]
    [InlineData("chooseAccount=1,consent")]
    [InlineData("chooseAccount=1%26prompt%3Dconsent")]
    [InlineData("chooseAccount=1&chooseAccount=consent")]
    [InlineData("login_hint=someone%40example.test")]
    [InlineData("hd=example.test&scope=profile&client_id=other")]
    [InlineData("redirect_uri=https%3A%2F%2Fevil.example.test%2F")]
    [InlineData("returnUrl=%2F%26prompt%3Dconsent")]
    public async Task HostileQuery_WithoutTheFlag_AddsNothing(string query)
    {
        var plain = await _host.GetAsync("/signin");

        var hostile = await _host.GetAsync("/signin?" + query);

        Assert.Empty(Prompts(hostile));
        Assert.Equal(Fixed(plain), Fixed(hostile));
    }

    [Theory]
    [InlineData("prompt=consent")]
    [InlineData("prompt=none")]
    [InlineData("login_hint=someone%40example.test")]
    [InlineData("hd=example.test&scope=profile&client_id=other")]
    [InlineData("redirect_uri=https%3A%2F%2Fevil.example.test%2F")]
    [InlineData("response_type=token&access_type=offline")]
    public async Task HostileQuery_WithTheFlag_ChangesNothing(string query)
    {
        var plain = await _host.GetAsync(Picker);

        var hostile = await _host.GetAsync(Picker + "&" + query);

        Assert.Equal(["select_account"], Prompts(hostile));
        Assert.Equal(Fixed(plain), Fixed(hostile));
    }

    [Theory]
    [MemberData(
        nameof(SignInTests.ReturnUrls),
        MemberType = typeof(SignInTests))]
    public async Task ReturnUrl_ThroughThePicker_StaysOnThisSite(
        string asked,
        string given)
    {
        var challenge = await _host.GetAsync(
            Picker + "&returnUrl=" + Uri.EscapeDataString(asked));
        Assert.Equal(["select_account"], Prompts(challenge));

        var callback = await FinishAsync(challenge);

        Assert.Equal(given, callback.Headers.Location!.ToString());
    }

    [Fact]
    public async Task SignIn_CompletesThroughThePicker()
    {
        var challenge = await _host.GetAsync(Picker);
        var query = WebUiHost.Query(challenge.Headers.Location!);
        Assert.Equal("S256", query["code_challenge_method"]);

        var callback = await FinishAsync(challenge);

        Assert.Equal("/", callback.Headers.Location!.ToString());
        Assert.True(_host.Cookies.ContainsKey(Cookie));
        Assert.Equal(
            HttpStatusCode.OK,
            (await _host.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task AnotherAccount_ThroughThePicker_IsRefused()
    {
        var challenge = await _host.GetAsync(Picker);

        var callback = await FinishAsync(
            challenge,
            "someone@example.test",
            "2");

        Assert.Equal(
            OwnerAuthentication.AccessDeniedPath,
            callback.Headers.Location!.ToString());
        Assert.False(_host.Cookies.ContainsKey(Cookie));
    }

    // Opening the link is a GET: it must not sign the owner out.
    [Fact]
    public async Task Picker_WhenSignedIn_GoesStraightBack()
    {
        await _host.SignInAsync();

        var again = await _host.GetAsync(
            Picker + "&returnUrl=%2Fnot-found");

        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Equal("/not-found", again.Headers.Location!.ToString());
        Assert.True(_host.Cookies.ContainsKey(Cookie));
        Assert.Equal(
            HttpStatusCode.OK,
            (await _host.GetAsync("/")).StatusCode);
    }
}
