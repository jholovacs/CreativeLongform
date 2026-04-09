using System.Text.Json.Serialization;

namespace CreativeLongform.Application.WorldBuilding;

public sealed class MeasurementSystemPayload
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("calendar")]
    public MeasurementCalendarDto? Calendar { get; set; }

    [JsonPropertyName("units")]
    public List<MeasurementUnitDto>? Units { get; set; }

    [JsonPropertyName("money")]
    public List<MeasurementMoneyDto>? Money { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public sealed class MeasurementCalendarDto
{
    [JsonPropertyName("daysPerYear")]
    public int? DaysPerYear { get; set; }

    [JsonPropertyName("daysPerWeek")]
    public int? DaysPerWeek { get; set; }

    [JsonPropertyName("monthNames")]
    public List<string>? MonthNames { get; set; }

    [JsonPropertyName("weekdayNames")]
    public List<string>? WeekdayNames { get; set; }
}

public sealed class MeasurementUnitDto
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("definition")]
    public string Definition { get; set; } = string.Empty;

    [JsonPropertyName("approximateSiNote")]
    public string? ApproximateSiNote { get; set; }
}

public sealed class MeasurementMoneyDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("definition")]
    public string Definition { get; set; } = string.Empty;
}
