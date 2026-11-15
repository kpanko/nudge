using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Nudge.Web.Tests;

/// <summary>Where the owner's time zone comes from.</summary>
public sealed class OwnerTimeZoneTests
{
    private static OwnerTimeZone From(
        string? id,
        string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [OwnerTimeZone.Setting] = id,
                })
            .Build();
        return OwnerTimeZone.From(
            configuration,
            new HostingEnvironment { EnvironmentName = environment });
    }

    [Fact]
    public void Zone_IsTheOneConfigured()
    {
        var zone = From("America/New_York", Environments.Production);

        Assert.Equal("America/New_York", zone.Zone.Id);
        Assert.True(zone.Configured);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Zone_IsRequired_OutsideDevelopment(string? id)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => From(id, Environments.Production));

        Assert.Contains(OwnerTimeZone.Setting, error.Message);
    }

    [Fact]
    public void Development_FallsBackToTheMachinesZone()
    {
        var zone = From(null, Environments.Development);

        Assert.Equal(TimeZoneInfo.Local.Id, zone.Zone.Id);
        Assert.False(zone.Configured);
        Assert.Contains("not set", zone.ToString());
    }

    [Theory]
    [InlineData("Nowhere/Special")]
    [InlineData("Eastern Standard Time")]
    public void Zone_MustBeAnIanaIdThatExists(string id)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => From(id, Environments.Development));

        Assert.Contains(id, error.Message);
    }

    [Fact]
    public void App_DoesNotStart_WithoutAZone_OutsideDevelopment()
    {
        using var factory = new NudgeWebFactory();
        using var production = factory.WithWebHostBuilder(
            builder => builder
                .UseEnvironment(Environments.Production)
                .UseSetting(OwnerTimeZone.Setting, ""));

        var error = Assert.ThrowsAny<Exception>(() => production.Services);

        Assert.Contains(OwnerTimeZone.Setting, error.ToString());
    }
}
