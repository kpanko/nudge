using System.Net;

namespace Nudge.Web.Tests;

public class HealthEndpointTests(NudgeWebFactory factory)
    : IClassFixture<NudgeWebFactory>
{
    [Fact]
    public async Task Healthz_ReturnsOk()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body);
    }
}
