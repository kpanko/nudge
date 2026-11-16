using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Nudge.Web.Api;
using Nudge.Web.Auth;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// The rules behind the sign-in, on their own: who
/// <see cref="OwnerSignIn"/> lets in, what a change made by the owner
/// is recorded as, and which forwarded headers the app believes.
/// </summary>
public sealed class OwnerRulesTests
{
    private static OwnerSignIn SignIn(
        string? email = WebUiHost.OwnerEmail,
        string? subject = null,
        string? clientId = WebUiHost.ClientId,
        string? secret = WebUiHost.ClientSecret)
    {
        return OwnerSignIn.From(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [OwnerSignIn.ClientIdSetting] = clientId,
                        [OwnerSignIn.ClientSecretSetting] = secret,
                        [OwnerSignIn.EmailSetting] = email,
                        [OwnerSignIn.SubjectSetting] = subject,
                    })
                .Build());
    }

    [Theory]
    [InlineData("owner@example.test", true, true)]
    [InlineData("OWNER@EXAMPLE.TEST", true, true)]
    [InlineData(" owner@example.test ", true, true)]
    [InlineData("owner@example.test", false, false)]
    [InlineData("someone@example.test", true, false)]
    [InlineData("owner@example.test.evil", true, false)]
    [InlineData("", true, false)]
    [InlineData(null, true, false)]
    public void Allows_OnlyTheOwnersVerifiedAddress(
        string? email,
        bool verified,
        bool allowed)
    {
        Assert.Equal(allowed, SignIn().Allows(email, verified, "1"));
    }

    [Fact]
    public void Allows_WithASubjectPinned_OnlyThatSubject()
    {
        var pinned = SignIn(subject: "42");

        Assert.True(pinned.Allows(WebUiHost.OwnerEmail, true, "42"));
        Assert.False(pinned.Allows(WebUiHost.OwnerEmail, true, "43"));
        Assert.False(pinned.Allows(WebUiHost.OwnerEmail, true, null));
    }

    [Theory]
    [InlineData(null, WebUiHost.ClientId, WebUiHost.ClientSecret)]
    [InlineData(WebUiHost.OwnerEmail, null, WebUiHost.ClientSecret)]
    [InlineData(WebUiHost.OwnerEmail, WebUiHost.ClientId, null)]
    [InlineData(" ", WebUiHost.ClientId, WebUiHost.ClientSecret)]
    public void NotConfigured_AllowsNobody(
        string? email,
        string? clientId,
        string? secret)
    {
        var signIn = SignIn(email, clientId: clientId, secret: secret);

        Assert.False(signIn.Configured);
        Assert.False(signIn.Allows(WebUiHost.OwnerEmail, true, "1"));
        Assert.False(signIn.Allows(email, true, "1"));
    }

    [Fact]
    public void ChangeSource_OfTheOwner_IsWeb()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(OwnerAuthentication.EmailClaim, "x")],
                    OwnerAuthentication.IdentityType)),
        };

        Assert.Equal("web", new ChangeSource().For(http));
    }

    [Fact]
    public void ChangeSource_OfAToken_IsStillTheTokensName()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "planning-agent")],
                    ApiTokenAuthentication.Scheme)),
        };

        Assert.Equal("api:planning-agent", new ChangeSource().For(http));
    }

    [Fact]
    public void ChangeSource_OfAnyoneElse_IsRefused()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(OwnerAuthentication.EmailClaim, "x")],
                    "SomethingElse")),
        };

        Assert.Throws<InvalidOperationException>(
            () => new ChangeSource().For(http));
    }

    [Fact]
    public async Task ForwardedProto_IsBelieved_ButNotForwardedHost()
    {
        using var host = new WebUiHost();
        using var http = host.CreateClient(new Uri("http://localhost"));

        async Task<string> RedirectUri(params (string, string)[] headers)
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Get,
                "/signin");
            foreach (var (name, value) in headers)
            {
                message.Headers.Add(name, value);
            }

            var reply = await http.SendAsync(message);
            Assert.Equal(HttpStatusCode.Redirect, reply.StatusCode);
            return WebUiHost.Query(reply.Headers.Location!)["redirect_uri"];
        }

        Assert.Equal(
            "http://localhost/signin-google",
            await RedirectUri());
        Assert.Equal(
            "https://localhost/signin-google",
            await RedirectUri(("X-Forwarded-Proto", "https")));
        Assert.Equal(
            "https://localhost/signin-google",
            await RedirectUri(
                ("X-Forwarded-Proto", "https"),
                ("X-Forwarded-Host", "evil.example.test"),
                ("X-Forwarded-For", "203.0.113.9")));
    }
}
