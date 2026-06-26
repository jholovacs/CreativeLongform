using System.Text;
using System.Text.RegularExpressions;

namespace CreativeLongform.Application.Agent;

/// <summary>Deterministic scene-brief beat checklist for <c>check_scene_brief</c>.</summary>
public static partial class AgentSceneBriefChecker
{
    public sealed record BeatCheck(string Beat, bool LikelyPresent, string Hint);

    public static string Run(string draft, string sceneInstructions, string? expectedEndNotes)
    {
        var beats = ExtractBeats(sceneInstructions, expectedEndNotes);
        if (beats.Count == 0)
            return "check_scene_brief: no parseable beats from scene instructions — rely on run_compliance_check.";

        var checks = beats.Select(b => EvaluateBeat(draft, b)).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("check_scene_brief result:");
        foreach (var c in checks)
        {
            sb.AppendLine($"  [{(c.LikelyPresent ? "ok" : "review")}] {c.Beat}");
            if (!c.LikelyPresent)
                sb.AppendLine($"       hint: {c.Hint}");
        }

        var missing = checks.Count(c => !c.LikelyPresent);
        sb.AppendLine($"  summary: {checks.Count - missing}/{checks.Count} beats likely present.");
        if (missing > 0)
            sb.AppendLine("  next: read_section / query_lore for missing beats, then invoke_writer or propose_patch.");
        return sb.ToString().TrimEnd();
    }

    private static List<string> ExtractBeats(string sceneInstructions, string? expectedEndNotes)
    {
        var beats = new List<string>();
        foreach (var line in sceneInstructions.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmed = BulletPrefix().Replace(line, "").Trim();
            if (trimmed.Length >= 12)
                beats.Add(trimmed);
        }

        if (!string.IsNullOrWhiteSpace(expectedEndNotes))
            beats.Add($"End state: {expectedEndNotes.Trim()}");

        foreach (Match m in SentenceBeatPattern().Matches(sceneInstructions))
        {
            var sentence = m.Value.Trim();
            if (sentence.Length >= 20 && beats.All(b => !b.Contains(sentence, StringComparison.OrdinalIgnoreCase)))
                beats.Add(sentence);
        }

        return beats.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
    }

    private static BeatCheck EvaluateBeat(string draft, string beat)
    {
        var tokens = beat.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 5 && char.IsLetter(w[0]))
            .Take(6)
            .ToList();
        if (tokens.Count == 0)
            return new BeatCheck(beat, true, "");

        var hits = tokens.Count(t => draft.Contains(t, StringComparison.OrdinalIgnoreCase));
        var ratio = (double)hits / tokens.Count;
        var present = ratio >= 0.45 || (tokens.Count <= 2 && hits >= 1);
        var hint = present
            ? ""
            : $"Look for keywords: {string.Join(", ", tokens.Take(4))}";
        return new BeatCheck(beat, present, hint);
    }

    [GeneratedRegex(@"^[\s\-*•\d.)]+")]
    private static partial Regex BulletPrefix();

    [GeneratedRegex(@"[^.!?]+[.!?]")]
    private static partial Regex SentenceBeatPattern();
}
