using System.Text;

namespace CreativeLongform.Application.Generation;

/// <summary>Detects and removes LLM repetition loops from scene draft prose.</summary>
public static class DraftProseGuard
{
    private const int MinParagraphCharsForRepeat = 40;
    private const int MinParagraphCharsForEcho = 15;
    private const int MinSuffixCharsForRepeat = 80;

    /// <summary>Removes trailing loops where paragraphs or large suffix chunks repeat earlier content.</summary>
    public static string TrimRepetitiveLoops(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = TrimDuplicateParagraphs(text.Trim());
        trimmed = TrimDuplicateSuffix(trimmed);
        trimmed = TrimConsecutiveDuplicateSentences(trimmed);
        return trimmed.Trim();
    }

    /// <summary>
    /// Drops an opening paragraph that reads like a restated state-table inventory (pose, clothing, mood, etc.).
    /// </summary>
    public static string TrimOpeningStateRecitation(string? prose, string? stateJson)
    {
        if (string.IsNullOrWhiteSpace(prose))
            return string.Empty;

        var phrases = CollectStateEchoPhrases(stateJson);
        if (phrases.Count == 0)
            return prose.Trim();

        var paragraphs = SplitParagraphs(prose.Trim());
        if (paragraphs.Count <= 1)
            return prose.Trim();

        if (!ParagraphEchoesStateTable(paragraphs[0], phrases))
            return prose.Trim();

        paragraphs.RemoveAt(0);
        return paragraphs.Count == 0 ? prose.Trim() : JoinParagraphs(paragraphs);
    }

    /// <summary>Merges continuation-only expansion output onto the existing draft without echoing it.</summary>
    public static string MergeDraftContinuation(string draft, string modelOutput)
    {
        draft = draft.Trim();
        var continuation = modelOutput.Trim();
        if (string.IsNullOrWhiteSpace(continuation))
            return draft;
        if (string.IsNullOrWhiteSpace(draft))
            return TrimRepetitiveLoops(continuation);

        if (continuation.StartsWith(draft, StringComparison.Ordinal))
            return TrimRepetitiveLoops(continuation);

        if (continuation.Length > draft.Length && continuation.Contains(draft, StringComparison.Ordinal))
            return TrimRepetitiveLoops(continuation);

        continuation = TrimLeadingEchoOfDraft(draft, continuation);
        if (string.IsNullOrWhiteSpace(continuation))
            return TrimRepetitiveLoops(draft);

        if (continuation.StartsWith(draft, StringComparison.Ordinal))
            return TrimRepetitiveLoops(continuation);

        return TrimRepetitiveLoops($"{draft}\n\n{continuation}");
    }

    private static string TrimDuplicateParagraphs(string text)
    {
        var paragraphs = SplitParagraphs(text);
        if (paragraphs.Count <= 1)
            return text;

        var kept = new List<string>(paragraphs.Count);
        var seen = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var norm = Normalize(paragraph);
            if (norm.Length >= MinParagraphCharsForRepeat &&
                seen.Any(s => ParagraphsSimilar(s, norm)))
            {
                break;
            }

            kept.Add(paragraph);
            if (norm.Length >= MinParagraphCharsForRepeat)
                seen.Add(norm);
        }

        return kept.Count == 0 ? text : JoinParagraphs(kept);
    }

    private static string TrimDuplicateSuffix(string text)
    {
        var maxLen = text.Length / 2;
        for (var len = maxLen; len >= MinSuffixCharsForRepeat; len--)
        {
            if (len * 2 > text.Length)
                continue;

            var suffix = text[^len..];
            var beforeSuffix = text[..^len];
            if (beforeSuffix.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return beforeSuffix.TrimEnd();
        }

        return text;
    }

    private static string TrimConsecutiveDuplicateSentences(string text)
    {
        var sentences = SplitSentences(text);
        if (sentences.Count <= 1)
            return text;

        var kept = new List<string>(sentences.Count);
        string? prevNorm = null;
        foreach (var sentence in sentences)
        {
            var norm = Normalize(sentence);
            if (norm.Length >= 30 && prevNorm is not null && norm == prevNorm)
                break;

            kept.Add(sentence);
            prevNorm = norm.Length >= 30 ? norm : prevNorm;
        }

        return kept.Count == 0 ? text : string.Concat(kept);
    }

    private static string TrimLeadingEchoOfDraft(string draft, string continuation)
    {
        if (continuation.StartsWith(draft, StringComparison.Ordinal))
            return continuation[draft.Length..].TrimStart();

        var draftParas = SplitParagraphs(draft);
        var contParas = SplitParagraphs(continuation);
        if (draftParas.Count == 0 || contParas.Count == 0)
            return continuation;

        while (contParas.Count > 0)
        {
            var echo = false;
            var firstNorm = Normalize(contParas[0]);
            if (firstNorm.Length >= MinParagraphCharsForEcho)
            {
                foreach (var draftPara in draftParas)
                {
                    var d = Normalize(draftPara);
                    if (d.Length >= MinParagraphCharsForEcho && ParagraphsSimilar(d, firstNorm))
                    {
                        echo = true;
                        break;
                    }
                }
            }

            if (!echo)
                break;

            contParas.RemoveAt(0);
        }

        return contParas.Count == 0 ? string.Empty : JoinParagraphs(contParas);
    }

    private static bool ParagraphsSimilar(string a, string b)
    {
        if (a == b)
            return true;

        if (a.Length >= MinParagraphCharsForRepeat && b.Length >= MinParagraphCharsForRepeat)
        {
            if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
                return true;
        }

        return WordOverlapRatio(a, b) >= 0.88;
    }

    private static double WordOverlapRatio(string a, string b)
    {
        var wa = a.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var wb = b.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (wa.Length == 0 || wb.Length == 0)
            return 0;

        var setB = new HashSet<string>(wb, StringComparer.OrdinalIgnoreCase);
        var overlap = wa.Count(w => setB.Contains(w));
        return (2.0 * overlap) / (wa.Length + wb.Length);
    }

    private static string Normalize(string s) =>
        s.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Trim()
            .ToLowerInvariant();

    private static List<string> SplitParagraphs(string text)
    {
        var parts = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.None);
        var list = new List<string>();
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.Length > 0)
                list.Add(p);
        }

        if (list.Count == 0 && !string.IsNullOrWhiteSpace(text))
            list.Add(text.Trim());
        return list;
    }

    private static string JoinParagraphs(IReadOnlyList<string> paragraphs) =>
        string.Join("\n\n", paragraphs);

    private static List<string> SplitSentences(string text)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            sb.Append(c);
            if (c is '.' or '!' or '?' && (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1])))
            {
                list.Add(sb.ToString());
                sb.Clear();
            }
        }

        if (sb.Length > 0)
            list.Add(sb.ToString());

        return list.Count == 0 ? [text] : list;
    }

    private static List<string> CollectStateEchoPhrases(string? stateJson)
    {
        var phrases = new List<string>();
        if (!LlmJson.TryNormalizeStateJson(stateJson, out var normalized))
            return phrases;

        var state = LlmJson.Deserialize<Narrative.NarrativeState>(normalized);
        if (state is null)
            return phrases;

        foreach (var c in state.Characters)
        {
            AddPhrase(phrases, c.Pose);
            AddPhrase(phrases, c.Clothing);
            AddPhrase(phrases, c.EmotionalState);
            AddPhrase(phrases, c.RelativeToOthers);
            AddPhrase(phrases, c.Location);
            foreach (var t in c.TopOfMind)
                AddPhrase(phrases, t);
            foreach (var t in c.TraitsShownNotTold)
                AddPhrase(phrases, t);
        }

        if (state.Spatial is not null)
        {
            AddPhrase(phrases, state.Spatial.Layout);
            AddPhrase(phrases, state.Spatial.Proximity);
        }

        if (state.Environment is not null)
        {
            foreach (var s in state.Environment.Sensory)
                AddPhrase(phrases, s);
        }

        if (state.Dialogue is not null)
        {
            AddPhrase(phrases, state.Dialogue.Topic);
            foreach (var u in state.Dialogue.Unresolved)
                AddPhrase(phrases, u);
        }

        if (state.Knowledge is not null)
        {
            foreach (var b in state.Knowledge.PovBeliefs)
                AddPhrase(phrases, b);
            foreach (var f in state.Knowledge.OmniscientFacts)
                AddPhrase(phrases, f);
        }

        foreach (var p in state.PlotDevices)
            AddPhrase(phrases, p);

        return phrases;
    }

    private static void AddPhrase(List<string> phrases, string? value)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length < 12)
            return;
        if (!phrases.Any(p => string.Equals(p, t, StringComparison.OrdinalIgnoreCase)))
            phrases.Add(t);
    }

    private static bool ParagraphEchoesStateTable(string paragraph, IReadOnlyList<string> phrases)
    {
        var norm = Normalize(paragraph);
        if (norm.Length < 20)
            return false;

        var matches = 0;
        foreach (var phrase in phrases)
        {
            var p = Normalize(phrase);
            if (p.Length < 12)
                continue;
            if (norm.Contains(p, StringComparison.Ordinal))
                matches++;
        }

        if (matches >= 2)
            return true;

        return matches >= 1 && phrases.Any(p => p.Length >= 28 && norm.Contains(Normalize(p), StringComparison.Ordinal));
    }
}
