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

    [Fact]
    public void IsEmptyJsonObject_detects_empty_object()
    {
        Assert.True(LlmJson.IsEmptyJsonObject("{}"));
        Assert.True(LlmJson.IsEmptyJsonObject(" { } "));
        Assert.False(LlmJson.IsEmptyJsonObject("""{"pass":true}"""));
    }

    [Fact]
    public void DeserializeComplianceVerdict_empty_object_treats_as_pass()
    {
        var v = LlmJson.DeserializeComplianceVerdict("{}");
        Assert.True(v.Pass);
        Assert.Empty(v.Violations);
        Assert.Empty(v.FixInstructions);
    }

    [Fact]
    public void DeserializeComplianceVerdict_explicit_pass_false_respected()
    {
        var v = LlmJson.DeserializeComplianceVerdict("""{"pass":false,"violations":[],"fixInstructions":[]}""");
        Assert.False(v.Pass);
    }

    [Fact]
    public void DeserializeComplianceVerdict_missing_pass_with_violations_fails()
    {
        var v = LlmJson.DeserializeComplianceVerdict("""{"violations":["x"]}""");
        Assert.False(v.Pass);
    }

    private sealed class TestVerdict
    {
        public bool Pass { get; set; }
        public List<string>? Violations { get; set; }
    }
}
