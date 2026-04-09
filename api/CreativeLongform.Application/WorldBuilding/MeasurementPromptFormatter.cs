using System.Text;
using System.Text.Json;
using CreativeLongform.Application.Generation;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.WorldBuilding;

public static class MeasurementPromptFormatter
{
    private const int MaxTotalChars = 3500;

    /// <summary>LLM-facing block for units, calendar, and money. Empty if nothing to say.</summary>
    public static string Format(Book book)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Units and measures (use consistently in prose):");
        sb.AppendLine(PresetBaseline(book.MeasurementPreset));

        MeasurementSystemPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(book.MeasurementSystemJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<MeasurementSystemPayload>(
                    book.MeasurementSystemJson,
                    JsonOptions.Default);
            }
            catch
            {
                sb.AppendLine("(Custom measurement JSON could not be parsed; use preset baseline only.)");
            }
        }

        if (payload is not null)
        {
            AppendCalendar(sb, payload.Calendar);
            AppendUnits(sb, payload.Units);
            AppendMoney(sb, payload.Money);
            if (!string.IsNullOrWhiteSpace(payload.Notes))
                sb.AppendLine($"Author notes: {payload.Notes.Trim()}");
        }

        var text = sb.ToString().TrimEnd();
        return text.Length > MaxTotalChars ? text[..MaxTotalChars] + "…" : text;
    }

    private static string PresetBaseline(MeasurementPreset preset)
    {
        return preset switch
        {
            MeasurementPreset.EarthMetric =>
                "Preset: Earth, metric/SI. Calendar: typically 365 days per year, 7-day week unless overridden below. " +
                "Length: meter, kilometer; mass: gram, kilogram; temperature: Celsius; energy: joule; power: watt. " +
                "Use real-world currency names when the story is contemporary Earth unless custom money is defined below.",
            MeasurementPreset.EarthUsCustomary =>
                "Preset: Earth, US customary where appropriate. Calendar: typically 365 days per year, 7-day week unless overridden below. " +
                "Length: inch, foot, yard, mile; weight: ounce, pound; volume: fluid ounce, cup, pint, gallon; temperature: Fahrenheit. " +
                "Use real-world US currency when appropriate unless custom money is defined below.",
            MeasurementPreset.Custom =>
                "Preset: custom. Rely on the calendar, units, and money definitions below; do not assume Earth defaults unless stated.",
            _ => string.Empty
        };
    }

    private static void AppendCalendar(StringBuilder sb, MeasurementCalendarDto? cal)
    {
        if (cal is null)
            return;
        sb.Append("Calendar: ");
        var parts = new List<string>();
        if (cal.DaysPerYear is { } y)
            parts.Add($"{y} days per year");
        if (cal.DaysPerWeek is { } w)
            parts.Add($"{w} days per week");
        if (cal.MonthNames is { Count: > 0 } m)
            parts.Add("months: " + string.Join(", ", m));
        if (cal.WeekdayNames is { Count: > 0 } d)
            parts.Add("weekdays: " + string.Join(", ", d));
        if (parts.Count == 0)
            return;
        sb.AppendLine(string.Join("; ", parts) + ".");
    }

    private static void AppendUnits(StringBuilder sb, List<MeasurementUnitDto>? units)
    {
        if (units is null || units.Count == 0)
            return;
        sb.AppendLine("Custom / story-specific units:");
        foreach (var u in units)
        {
            if (string.IsNullOrWhiteSpace(u.Name))
                continue;
            var sym = string.IsNullOrWhiteSpace(u.Symbol) ? "" : $" ({u.Symbol})";
            var cat = string.IsNullOrWhiteSpace(u.Category) ? "Other" : u.Category;
            sb.AppendLine($"- [{cat}] {u.Name}{sym}: {u.Definition.Trim()}");
            if (!string.IsNullOrWhiteSpace(u.ApproximateSiNote))
                sb.AppendLine($"  Approximate real-world anchor: {u.ApproximateSiNote.Trim()}");
        }
    }

    private static void AppendMoney(StringBuilder sb, List<MeasurementMoneyDto>? money)
    {
        if (money is null || money.Count == 0)
            return;
        sb.AppendLine("Money / currency:");
        foreach (var m in money)
        {
            if (string.IsNullOrWhiteSpace(m.Name))
                continue;
            sb.AppendLine($"- {m.Name.Trim()}: {m.Definition.Trim()}");
        }
    }
}
