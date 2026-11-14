using System.Text.Json.Nodes;

namespace Nudge.Web.Tests;

internal static class JsonAssert
{
    /// <summary>The two texts are the same JSON, whatever order
    /// their properties come in.</summary>
    public static void Equal(
        string expected,
        string? actual)
    {
        Assert.NotNull(actual);
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse(expected),
                JsonNode.Parse(actual)),
            $"Expected {expected}{Environment.NewLine}but was {actual}");
    }
}
