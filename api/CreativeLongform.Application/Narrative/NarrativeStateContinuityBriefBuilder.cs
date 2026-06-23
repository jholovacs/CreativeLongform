using System.Text;
using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Narrative;

/// <summary>Minimal continuity anchor for draft prompts — avoids copy-paste fodder from full state JSON.</summary>
public static class NarrativeStateContinuityBriefBuilder
{
    public static string BuildForDraftPrompt(string? stateJson)
    {
        if (!LlmJson.TryNormalizeStateJson(stateJson, out var normalized))
            return "No beginning-state snapshot — infer who and where from the synopsis only. Do not open with a state inventory.";

        var state = LlmJson.Deserialize<NarrativeState>(normalized);
        if (state is null)
            return "No beginning-state snapshot — infer who and where from the synopsis only. Do not open with a state inventory.";

        var sb = new StringBuilder();
        sb.AppendLine("Continuity anchor (internal planning only — never quote, list, or restate in prose):");

        var names = state.Characters
            .Select(c => c.Name?.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count > 0)
            sb.AppendLine($"- People on stage: {string.Join(", ", names)}");

        var env = state.Environment;
        if (env is not null)
        {
            if (!string.IsNullOrWhiteSpace(env.Setting))
                sb.AppendLine($"- Place: {env.Setting.Trim()}");
            if (!string.IsNullOrWhiteSpace(env.TimeOfDay))
                sb.AppendLine($"- Time: {env.TimeOfDay.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(state.TransitionSummary))
            sb.AppendLine($"- Handoff from prior scene (fact, not narration): {state.TransitionSummary.Trim()}");

        var openThreads = new List<string>();
        if (state.Dialogue?.Unresolved is { Count: > 0 } unresolved)
        {
            foreach (var u in unresolved.Where(s => !string.IsNullOrWhiteSpace(s)).Take(4))
                openThreads.Add(u.Trim());
        }

        foreach (var c in state.Characters)
        {
            foreach (var t in c.TopOfMind.Where(s => !string.IsNullOrWhiteSpace(s)).Take(2))
                openThreads.Add(t.Trim());
        }

        if (openThreads.Count > 0)
        {
            var distinct = openThreads.Distinct(StringComparer.OrdinalIgnoreCase).Take(5);
            sb.AppendLine($"- Salient threads at entry (dramatize through action; do not recite): {string.Join("; ", distinct)}");
        }

        if (names.Count == 0 && string.IsNullOrWhiteSpace(env?.Setting))
            return "Beginning state is sparse — infer entry context from the synopsis. Start in motion; do not narrate a state inventory.";

        sb.AppendLine(
            "Full state-table fields (pose, clothing, mood labels, blocking, sensory lists) are omitted here on purpose — do not invent an opening that reads them aloud.");
        return sb.ToString().TrimEnd();
    }
}
