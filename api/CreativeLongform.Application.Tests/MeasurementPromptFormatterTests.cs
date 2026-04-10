using CreativeLongform.Application.WorldBuilding;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.Tests;

/// <summary>
/// Unit tests for <see cref="MeasurementPromptFormatter"/>, which turns <see cref="Book"/> measurement settings into prompt text for the LLM.
/// </summary>
public class MeasurementPromptFormatterTests
{
    /// <summary>
    /// <para><b>System under test:</b> <see cref="MeasurementPromptFormatter.Format"/> with <see cref="MeasurementPreset.EarthMetric"/>.</para>
    /// <para><b>Test case:</b> Book has only preset (no custom JSON).</para>
    /// <para><b>Expected result:</b> Output mentions metric units and meters (baseline for lengths).</para>
    /// <para><b>Why it matters:</b> Wrong preset text would confuse the model about real-world vs fictional units.</para>
    /// </summary>
    [Fact]
    public void Format_includes_preset_baseline_for_metric()
    {
        var book = new Book { MeasurementPreset = MeasurementPreset.EarthMetric };
        var text = MeasurementPromptFormatter.Format(book);
        Assert.Contains("metric", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meter", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="MeasurementPromptFormatter.Format"/> with <see cref="MeasurementPreset.Custom"/> and <see cref="Book.MeasurementSystemJson"/>.</para>
    /// <para><b>Test case:</b> Valid JSON defining calendar, custom length unit, and currency.</para>
    /// <para><b>Expected result:</b> Key numbers and names appear in the formatted prompt.</para>
    /// <para><b>Why it matters:</b> Custom worlds rely on this blob; omissions mean the model ignores user-defined calendars and money.</para>
    /// </summary>
    [Fact]
    public void Format_appends_custom_json_calendar_and_units()
    {
        var json = """
                   {"schemaVersion":1,"calendar":{"daysPerYear":400,"daysPerWeek":10,"monthNames":["A","B"]},"units":[{"category":"Length","name":"span","symbol":"sp","definition":"king's stride"}],"money":[{"name":"drak","definition":"silver coin"}],"notes":"test"}
                   """;
        var book = new Book
        {
            MeasurementPreset = MeasurementPreset.Custom,
            MeasurementSystemJson = json
        };
        var text = MeasurementPromptFormatter.Format(book);
        Assert.Contains("400", text, StringComparison.Ordinal);
        Assert.Contains("span", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drak", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="MeasurementPromptFormatter.Format"/> error handling for bad JSON.</para>
    /// <para><b>Test case:</b> <see cref="MeasurementPreset.EarthUsCustomary"/> with non-JSON <see cref="Book.MeasurementSystemJson"/>.</para>
    /// <para><b>Expected result:</b> Parse warning appears; preset baseline (US customary) still included.</para>
    /// <para><b>Why it matters:</b> Invalid custom JSON should not blank the entire measurement section or crash prompt building.</para>
    /// </summary>
    [Fact]
    public void Format_invalid_json_adds_parse_note_but_keeps_preset()
    {
        var book = new Book
        {
            MeasurementPreset = MeasurementPreset.EarthUsCustomary,
            MeasurementSystemJson = "{not json"
        };
        var text = MeasurementPromptFormatter.Format(book);
        Assert.Contains("could not be parsed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("US customary", text, StringComparison.OrdinalIgnoreCase);
    }
}
