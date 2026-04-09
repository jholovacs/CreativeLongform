using CreativeLongform.Application.WorldBuilding;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.Tests;

public class MeasurementPromptFormatterTests
{
    [Fact]
    public void Format_includes_preset_baseline_for_metric()
    {
        var book = new Book { MeasurementPreset = MeasurementPreset.EarthMetric };
        var text = MeasurementPromptFormatter.Format(book);
        Assert.Contains("metric", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meter", text, StringComparison.OrdinalIgnoreCase);
    }

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
