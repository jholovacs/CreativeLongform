using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Tests;

public class LlmJsonTests
{
    [Fact]
    public void StripMarkdownFences_returns_plain_json_when_no_fence()
    {
        const string raw = """{"pass":true}""";
        Assert.Equal(raw, LlmJson.StripMarkdownFences(raw));
    }

    [Fact]
    public void StripMarkdownFences_strips_triple_backtick_fence()
    {
        var input = """
            ```json
            {"pass":true}
            ```
            """;
        var result = LlmJson.StripMarkdownFences(input);
        Assert.Contains("\"pass\":true", result, StringComparison.Ordinal);
        Assert.DoesNotContain("```", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_parses_verdict_shape()
    {
        const string json = """{"pass":false,"violations":["a"]}""";
        var v = LlmJson.Deserialize<TestVerdict>(json);
        Assert.NotNull(v);
        Assert.False(v.Pass);
        Assert.Single(v.Violations!);
    }

    private sealed class TestVerdict
    {
        public bool Pass { get; set; }
        public List<string>? Violations { get; set; }
    }
}
