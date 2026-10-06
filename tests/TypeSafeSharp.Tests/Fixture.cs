using System.Text.Json.Nodes;

namespace TypeSafeSharp.Tests;

// Fixtures are copied next to the test assembly; the documented ones are the spec (Appendix B).
internal static class Fixture
{
    public static byte[] Bytes(string relativePath)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath));

    public static JsonNode Json(string relativePath)
        => JsonNode.Parse(Bytes(relativePath))!;

    public static void AssertJsonEqual(JsonNode expected, byte[] actual)
    {
        var parsed = JsonNode.Parse(actual);
        Assert.True(JsonNode.DeepEquals(expected, parsed), $"Expected {expected.ToJsonString()} but got {parsed?.ToJsonString()}");
    }
}
