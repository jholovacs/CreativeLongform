using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Tests;

/// <summary>
/// Unit tests for <see cref="LlmJson"/> helpers that strip markdown fences and deserialize structured LLM outputs (including compliance verdicts).
/// </summary>
public class LlmJsonTests
{
    /// <summary>
    /// <para><b>System under test:</b> <see cref="LlmJson.StripMarkdownFences"/>.</para>
    /// <para><b>Test case:</b> Input is already bare JSON with no markdown wrapper.</para>
    /// <para><b>Expected result:</b> String unchanged.</para>
    /// <para><b>Why it matters:</b> Most code paths pass through unchanged JSON; unnecessary mutation would break parsing.</para>
    /// </summary>
    [Fact]
    public void StripMarkdownFences_returns_plain_json_when_no_fence()
    {
        const string raw = """{"pass":true}""";
        Assert.Equal(raw, LlmJson.StripMarkdownFences(raw));
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="LlmJson.StripMarkdownFences"/>.</para>
    /// <para><b>Test case:</b> Model wraps JSON in a <c>```json</c> fence.</para>
    /// <para><b>Expected result:</b> Fence markers removed; JSON payload remains parseable.</para>
    /// <para><b>Why it matters:</b> Chat models often fence output; failing to strip causes <see cref="System.Text.Json"/> parse failures downstream.</para>
    /// </summary>
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

    /// <summary>
    /// <para><b>System under test:</b> <see cref="LlmJson.Deserialize{T}"/>.</para>
    /// <para><b>Test case:</b> Deserialize a simple verdict-shaped JSON into a test DTO.</para>
    /// <para><b>Expected result:</b> <c>Pass</c> false and one violation string.</para>
    /// <para><b>Why it matters:</b> Confirms generic deserialization works for pipeline verdict types.</para>
    /// </summary>
    [Fact]
    public void Deserialize_parses_verdict_shape()
    {
        const string json = """{"pass":false,"violations":["a"]}""";
        var v = LlmJson.Deserialize<TestVerdict>(json);
        Assert.NotNull(v);
        Assert.False(v.Pass);
        Assert.Single(v.Violations!);
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="LlmJson.IsEmptyJsonObject"/>.</para>
    /// <para><b>Test case:</b> <c>{}</c>, whitespace-padded <c>{ }</c>, and non-empty object.</para>
    /// <para><b>Expected result:</b> True for empty object forms; false when a property is present.</para>
    /// <para><b>Why it matters:</b> Used to detect degenerate model output; misclassification could treat real failures as passes.</para>
    /// </summary>
    [Fact]
    public void IsEmptyJsonObject_detects_empty_object()
    {
        Assert.True(LlmJson.IsEmptyJsonObject("{}"));
        Assert.True(LlmJson.IsEmptyJsonObject(" { } "));
        Assert.False(LlmJson.IsEmptyJsonObject("""{"pass":true}"""));
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="LlmJson.DeserializeComplianceVerdict"/>.</para>
    /// <para><b>Test case:</b> Model returns <c>{}</c> with no <c>pass</c> field.</para>
    /// <para><b>Expected result:</b> Treated as pass with empty violations and fix instructions.</para>
    /// <para><b>Why it matters:</b> Empty object is a documented “silent pass” for noisy models; changing this would block generation on benign empty replies.</para>
    /// </summary>
    [Fact]
    public void DeserializeComplianceVerdict_empty_object_treats_as_pass()
    {
        var v = LlmJson.DeserializeComplianceVerdict("{}");
        Assert.True(v.Pass);
        Assert.Empty(v.Violations);
        Assert.Empty(v.FixInstructions);
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="LlmJson.DeserializeComplianceVerdict"/>.</para>
    /// <para><b>Test case:</b> Explicit <c>pass: false</c> with empty violation lists.</para>
    /// <para><b>Expected result:</b> <c>Pass</c> is false.</para>
    /// <para><b>Why it matters:</b> Ensures explicit failures are not overridden by empty collections.</para>
    /// </summary>
    [Fact]
    public void DeserializeComplianceVerdict_explicit_pass_false_respected()
    {
        var v = LlmJson.DeserializeComplianceVerdict("""{"pass":false,"violations":[],"fixInstructions":[]}""");
        Assert.False(v.Pass);
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="LlmJson.DeserializeComplianceVerdict"/> when <c>pass</c> is absent.</para>
    /// <para><b>Test case:</b> JSON has <c>violations</c> but no <c>pass</c> property.</para>
    /// <para><b>Expected result:</b> Interpreted as failure (<c>Pass</c> false).</para>
    /// <para><b>Why it matters:</b> Violations without an explicit pass must not be treated as success.</para>
    /// </summary>
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
