using System.Text;
using System.Text.RegularExpressions;

namespace CreativeLongform.Application.Generation;

/// <summary>Non-LLM text search/replace helpers for the agentic edit loop.</summary>
public static class AgentDraftTextTools
{
    private const int DefaultMaxMatches = 40;
    private const int DefaultMaxReplacements = 200;
    private const int ExcerptRadius = 40;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public sealed record TextMatch(int ParagraphIndex, int CharOffset, string Excerpt);

    public sealed record FindTextResult(bool Ok, string Message, IReadOnlyList<TextMatch> Matches);

    public sealed record ReplaceTextResult(
        bool Ok,
        string Message,
        int ReplacementsApplied,
        IReadOnlyList<int> ParagraphsModified,
        IReadOnlyList<string> Samples);

    public static FindTextResult Find(
        IReadOnlyList<string> paragraphs,
        string pattern,
        bool useRegex,
        bool caseSensitive,
        int? maxMatches,
        int? paragraphStart,
        int? paragraphEnd)
    {
        if (string.IsNullOrEmpty(pattern))
            return new FindTextResult(false, "Error: pattern/query is required.", Array.Empty<TextMatch>());

        var limit = Math.Clamp(maxMatches ?? DefaultMaxMatches, 1, 200);
        var (start, end) = ResolveParagraphScope(paragraphs.Count, paragraphStart, paragraphEnd);
        if (start < 0)
            return new FindTextResult(false, "Error: invalid paragraph scope.", Array.Empty<TextMatch>());

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = new List<TextMatch>();

        try
        {
            if (useRegex)
            {
                var opts = RegexOptions.CultureInvariant | RegexOptions.Multiline;
                if (!caseSensitive)
                    opts |= RegexOptions.IgnoreCase;
                var rx = new Regex(pattern, opts, RegexTimeout);

                for (var p = start; p <= end && matches.Count < limit; p++)
                {
                    var text = paragraphs[p];
                    foreach (Match m in rx.Matches(text))
                    {
                        if (!m.Success)
                            continue;
                        matches.Add(new TextMatch(p, m.Index, BuildExcerpt(text, m.Index, m.Length)));
                        if (matches.Count >= limit)
                            break;
                    }
                }
            }
            else
            {
                for (var p = start; p <= end && matches.Count < limit; p++)
                {
                    var text = paragraphs[p];
                    var idx = 0;
                    while (idx <= text.Length && matches.Count < limit)
                    {
                        var found = text.IndexOf(pattern, idx, comparison);
                        if (found < 0)
                            break;
                        matches.Add(new TextMatch(p, found, BuildExcerpt(text, found, pattern.Length)));
                        idx = found + Math.Max(1, pattern.Length);
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new FindTextResult(false, "Error: regex timed out — use a simpler pattern or literal find (useRegex:false).", Array.Empty<TextMatch>());
        }
        catch (Exception ex)
        {
            return new FindTextResult(false, $"Error: search failed — {ex.Message}", Array.Empty<TextMatch>());
        }

        return new FindTextResult(true, $"find_text: {matches.Count} match(es).", matches);
    }

    public static ReplaceTextResult Replace(
        IList<string> paragraphs,
        string pattern,
        string replacement,
        bool useRegex,
        bool caseSensitive,
        int? maxReplacements,
        int? paragraphStart,
        int? paragraphEnd,
        bool previewOnly)
    {
        if (string.IsNullOrEmpty(pattern))
            return new ReplaceTextResult(false, "Error: pattern is required.", 0, Array.Empty<int>(), Array.Empty<string>());

        replacement ??= "";
        var limit = Math.Clamp(maxReplacements ?? DefaultMaxReplacements, 1, 500);
        var (start, end) = ResolveParagraphScope(paragraphs.Count, paragraphStart, paragraphEnd);
        if (start < 0)
            return new ReplaceTextResult(false, "Error: invalid paragraph scope.", 0, Array.Empty<int>(), Array.Empty<string>());

        var modified = new List<int>();
        var samples = new List<string>();
        var total = 0;

        try
        {
            for (var p = start; p <= end && total < limit; p++)
            {
                var text = paragraphs[p];
                string newText;
                int count;

                if (useRegex)
                {
                    var opts = RegexOptions.CultureInvariant | RegexOptions.Multiline;
                    if (!caseSensitive)
                        opts |= RegexOptions.IgnoreCase;
                    var rx = new Regex(pattern, opts, RegexTimeout);
                    count = rx.Matches(text).Count;
                    if (count == 0)
                        continue;
                    count = Math.Min(count, limit - total);
                    newText = rx.Replace(text, replacement, count);
                }
                else
                {
                    var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    count = 0;
                    var sb = new StringBuilder();
                    var idx = 0;
                    while (idx <= text.Length && count < limit - total)
                    {
                        var found = text.IndexOf(pattern, idx, comparison);
                        if (found < 0)
                        {
                            sb.Append(text, idx, text.Length - idx);
                            break;
                        }

                        sb.Append(text, idx, found - idx);
                        sb.Append(replacement);
                        if (samples.Count < 5)
                            samples.Add($"¶{p}: \"{TruncateForSample(text, found, pattern.Length)}\" → \"{TruncateForSample(replacement, 0, replacement.Length)}\"");
                        idx = found + pattern.Length;
                        count++;
                    }

                    if (count == 0)
                        continue;
                    newText = sb.ToString();
                }

                if (string.Equals(text, newText, StringComparison.Ordinal))
                    continue;

                if (previewOnly)
                {
                    if (samples.Count < 5 && useRegex)
                    {
                        var rxPreview = new Regex(pattern, RegexOptions.CultureInvariant | (caseSensitive ? 0 : RegexOptions.IgnoreCase), RegexTimeout);
                        var m = rxPreview.Match(text);
                        if (m.Success)
                            samples.Add($"¶{p} (preview): \"{TruncateForSample(text, m.Index, m.Length)}\" → \"{replacement}\"");
                    }

                    total += count;
                    modified.Add(p);
                    continue;
                }

                paragraphs[p] = newText;
                total += count;
                modified.Add(p);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new ReplaceTextResult(false, "Error: regex timed out — use a simpler pattern or literal replace.", 0, Array.Empty<int>(), Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new ReplaceTextResult(false, $"Error: replace failed — {ex.Message}", 0, Array.Empty<int>(), Array.Empty<string>());
        }

        if (total == 0)
            return new ReplaceTextResult(true, "replace_text: no matches found (draft unchanged).", 0, Array.Empty<int>(), Array.Empty<string>());

        var verb = previewOnly ? "would apply" : "applied";
        return new ReplaceTextResult(true, $"replace_text: {verb} {total} replacement(s) in {modified.Count} paragraph(s).", total, modified, samples);
    }

    public static string FormatFindResult(FindTextResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.Message);
        if (!result.Ok)
            return sb.ToString().TrimEnd();
        if (result.Matches.Count == 0)
        {
            sb.AppendLine("  (no matches)");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("  matches:");
        foreach (var m in result.Matches)
            sb.AppendLine($"    - ¶{m.ParagraphIndex}, offset {m.CharOffset}: {m.Excerpt}");
        return sb.ToString().TrimEnd();
    }

    public static string FormatReplaceResult(ReplaceTextResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.Message);
        if (!result.Ok)
            return sb.ToString().TrimEnd();
        if (result.ParagraphsModified.Count > 0)
            sb.AppendLine($"  paragraphsModified: [{string.Join(", ", result.ParagraphsModified)}]");
        foreach (var s in result.Samples)
            sb.AppendLine($"  sample: {s}");
        return sb.ToString().TrimEnd();
    }

    public sealed record SwapTextResult(
        bool Ok,
        string Message,
        IReadOnlyList<int> ParagraphsModified,
        string SampleA,
        string SampleB);

    public static SwapTextResult Swap(
        IList<string> paragraphs,
        string selectionA,
        string selectionB,
        bool useRegex,
        bool caseSensitive,
        int? paragraphStart,
        int? paragraphEnd,
        bool previewOnly)
    {
        if (string.IsNullOrWhiteSpace(selectionA) || string.IsNullOrWhiteSpace(selectionB))
            return new SwapTextResult(false, "Error: swap_text requires two non-empty selections (excerptA and excerptB).", Array.Empty<int>(), "", "");

        var (start, end) = ResolveParagraphScope(paragraphs.Count, paragraphStart, paragraphEnd);
        if (start < 0)
            return new SwapTextResult(false, "Error: invalid paragraph scope.", Array.Empty<int>(), "", "");

        try
        {
            var locA = LocateFirst(paragraphs, start, end, selectionA, useRegex, caseSensitive);
            if (locA is null)
                return new SwapTextResult(false, "Error: swap_text could not locate selection A in the draft.", Array.Empty<int>(), "", "");

            var locB = LocateFirst(paragraphs, start, end, selectionB, useRegex, caseSensitive);
            if (locB is null)
                return new SwapTextResult(false, "Error: swap_text could not locate selection B in the draft.", Array.Empty<int>(), "", "");

            if (RangesOverlap(locA, locB))
                return new SwapTextResult(false, "Error: swap_text selections overlap — choose distinct, non-overlapping excerpts.", Array.Empty<int>(), "", "");

            if (string.Equals(locA.MatchedText, locB.MatchedText, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                return new SwapTextResult(true, "swap_text: selections are identical (draft unchanged).", Array.Empty<int>(), locA.MatchedText, locB.MatchedText);

            var sampleA = FormatSwapSample(locA);
            var sampleB = FormatSwapSample(locB);

            if (previewOnly)
                return new SwapTextResult(true, $"swap_text: would swap {sampleA} with {sampleB}.", Array.Empty<int>(), locA.MatchedText, locB.MatchedText);

            if (locA.ParagraphIndex == locB.ParagraphIndex)
                paragraphs[locA.ParagraphIndex] = SwapWithinParagraph(paragraphs[locA.ParagraphIndex], locA, locB);
            else
            {
                paragraphs[locA.ParagraphIndex] = ReplaceAt(paragraphs[locA.ParagraphIndex], locA, locB.MatchedText);
                paragraphs[locB.ParagraphIndex] = ReplaceAt(paragraphs[locB.ParagraphIndex], locB, locA.MatchedText);
            }

            var modified = locA.ParagraphIndex == locB.ParagraphIndex
                ? new[] { locA.ParagraphIndex }
                : new[] { locA.ParagraphIndex, locB.ParagraphIndex };
            return new SwapTextResult(true, $"swap_text: swapped {sampleA} with {sampleB}.", modified, locA.MatchedText, locB.MatchedText);
        }
        catch (RegexMatchTimeoutException)
        {
            return new SwapTextResult(false, "Error: regex timed out in swap_text — use literal excerpts (useRegex:false).", Array.Empty<int>(), "", "");
        }
        catch (Exception ex)
        {
            return new SwapTextResult(false, $"Error: swap_text failed — {ex.Message}", Array.Empty<int>(), "", "");
        }
    }

    public static string FormatSwapResult(SwapTextResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.Message);
        if (!result.Ok)
            return sb.ToString().TrimEnd();
        if (result.ParagraphsModified.Count > 0)
            sb.AppendLine($"  paragraphsModified: [{string.Join(", ", result.ParagraphsModified)}]");
        if (!string.IsNullOrEmpty(result.SampleA) && !string.IsNullOrEmpty(result.SampleB))
            sb.AppendLine($"  swapped: \"{TruncateForSample(result.SampleA, 0, result.SampleA.Length)}\" ↔ \"{TruncateForSample(result.SampleB, 0, result.SampleB.Length)}\"");
        return sb.ToString().TrimEnd();
    }

    public sealed record PatchTextResult(bool Ok, string Message, IReadOnlyList<int> ParagraphsModified);

    public static PatchTextResult Patch(
        IList<string> paragraphs,
        string mode,
        int paragraphStart,
        int? paragraphEnd,
        string? excerptOrPattern,
        string? text,
        bool useRegex,
        bool caseSensitive)
    {
        var m = mode.Trim().ToLowerInvariant();
        var (start, end) = ResolveParagraphScope(paragraphs.Count, paragraphStart, paragraphEnd ?? paragraphStart);
        if (start < 0)
            return new PatchTextResult(false, "Error: invalid paragraph scope.", Array.Empty<int>());

        var payload = text ?? "";
        var modified = new List<int>();

        try
        {
            if (m is "append_paragraph")
            {
                if (string.IsNullOrEmpty(payload))
                    return new PatchTextResult(false, "Error: append_paragraph requires text.", Array.Empty<int>());
                paragraphs.Insert(end + 1, payload.Trim());
                return new PatchTextResult(true, $"patch_text: appended new paragraph after ¶{end}.", new[] { end + 1 });
            }

            if (m is "prepend_paragraph")
            {
                if (string.IsNullOrEmpty(payload))
                    return new PatchTextResult(false, "Error: prepend_paragraph requires text.", Array.Empty<int>());
                paragraphs.Insert(start, payload.Trim());
                return new PatchTextResult(true, $"patch_text: prepended new paragraph before ¶{start}.", new[] { start });
            }

            var needle = excerptOrPattern ?? "";
            if (string.IsNullOrEmpty(needle))
                return new PatchTextResult(false, "Error: patch_text requires excerpt or pattern for this mode.", Array.Empty<int>());

            for (var p = start; p <= end; p++)
            {
                var original = paragraphs[p];
                var updated = ApplyPatchToParagraph(original, m, needle, payload, useRegex, caseSensitive);
                if (updated is null)
                    continue;
                if (!string.Equals(original, updated, StringComparison.Ordinal))
                {
                    paragraphs[p] = updated;
                    modified.Add(p);
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new PatchTextResult(false, "Error: regex timed out in patch_text.", Array.Empty<int>());
        }
        catch (Exception ex)
        {
            return new PatchTextResult(false, $"Error: patch_text failed — {ex.Message}", Array.Empty<int>());
        }

        if (modified.Count == 0)
            return new PatchTextResult(true, "patch_text: no matching excerpt found (draft unchanged).", Array.Empty<int>());

        return new PatchTextResult(true, $"patch_text ({m}): updated paragraph(s) [{string.Join(", ", modified)}].", modified);
    }

    public static string FormatPatchResult(PatchTextResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.Message);
        return sb.ToString().TrimEnd();
    }

    private static string? ApplyPatchToParagraph(
        string paragraph,
        string mode,
        string needle,
        string payload,
        bool useRegex,
        bool caseSensitive)
    {
        if (useRegex)
        {
            var opts = RegexOptions.CultureInvariant;
            if (!caseSensitive)
                opts |= RegexOptions.IgnoreCase;
            var rx = new Regex(needle, opts, RegexTimeout);
            return mode switch
            {
                "replace_excerpt" => rx.Replace(paragraph, payload, 1),
                "remove_excerpt" => rx.Replace(paragraph, "", 1),
                "insert_before_excerpt" => rx.Replace(paragraph, payload + "$0", 1),
                "insert_after_excerpt" => rx.Replace(paragraph, "$0" + payload, 1),
                _ => null
            };
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var idx = paragraph.IndexOf(needle, comparison);
        if (idx < 0)
            return null;

        return mode switch
        {
            "replace_excerpt" => paragraph[..idx] + payload + paragraph[(idx + needle.Length)..],
            "remove_excerpt" => paragraph.Remove(idx, needle.Length),
            "insert_before_excerpt" => paragraph.Insert(idx, payload),
            "insert_after_excerpt" => paragraph.Insert(idx + needle.Length, payload),
            _ => null
        };
    }

    internal static (int Start, int End) ResolveParagraphScope(
        int paragraphCount,
        int? paragraphStart,
        int? paragraphEnd)
    {
        if (paragraphCount == 0)
            return (-1, -1);
        var start = paragraphStart ?? 0;
        var end = paragraphEnd ?? paragraphCount - 1;
        if (start < 0 || end < start || end >= paragraphCount)
            return (-1, -1);
        return (start, end);
    }

    private static string BuildExcerpt(string text, int index, int length)
    {
        var left = Math.Max(0, index - ExcerptRadius);
        var right = Math.Min(text.Length, index + length + ExcerptRadius);
        var core = text[index..Math.Min(text.Length, index + length)];
        var prefix = left > 0 ? "…" : "";
        var suffix = right < text.Length ? "…" : "";
        var before = text[left..index];
        var after = text[(index + length)..right];
        return $"{prefix}{before}>>{core}<<{after}{suffix}";
    }

    private static string TruncateForSample(string text, int start, int length)
    {
        var slice = length <= 0 ? "" : text.Substring(start, Math.Min(length, text.Length - start));
        if (slice.Length > 60)
            return slice[..60] + "…";
        return slice;
    }

    private sealed record LocatedSelection(int ParagraphIndex, int Start, int Length, string MatchedText);

    private static LocatedSelection? LocateFirst(
        IList<string> paragraphs,
        int scopeStart,
        int scopeEnd,
        string needle,
        bool useRegex,
        bool caseSensitive)
    {
        for (var p = scopeStart; p <= scopeEnd; p++)
        {
            var text = paragraphs[p];
            if (useRegex)
            {
                var opts = RegexOptions.CultureInvariant;
                if (!caseSensitive)
                    opts |= RegexOptions.IgnoreCase;
                var rx = new Regex(needle, opts, RegexTimeout);
                var m = rx.Match(text);
                if (m.Success)
                    return new LocatedSelection(p, m.Index, m.Length, m.Value);
            }
            else
            {
                var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var idx = text.IndexOf(needle, comparison);
                if (idx >= 0)
                    return new LocatedSelection(p, idx, needle.Length, text.Substring(idx, needle.Length));
            }
        }

        return null;
    }

    private static bool RangesOverlap(LocatedSelection a, LocatedSelection b)
    {
        if (a.ParagraphIndex != b.ParagraphIndex)
            return false;
        var aEnd = a.Start + a.Length;
        var bEnd = b.Start + b.Length;
        return a.Start < bEnd && b.Start < aEnd;
    }

    private static string SwapWithinParagraph(string paragraph, LocatedSelection a, LocatedSelection b)
    {
        var first = a.Start <= b.Start ? a : b;
        var second = a.Start <= b.Start ? b : a;
        var firstText = paragraph.Substring(first.Start, first.Length);
        var secondText = paragraph.Substring(second.Start, second.Length);
        var between = paragraph[(first.Start + first.Length)..second.Start];
        return paragraph[..first.Start] + secondText + between + firstText + paragraph[(second.Start + second.Length)..];
    }

    private static string ReplaceAt(string paragraph, LocatedSelection loc, string replacement) =>
        paragraph[..loc.Start] + replacement + paragraph[(loc.Start + loc.Length)..];

    private static string FormatSwapSample(LocatedSelection loc) =>
        $"¶{loc.ParagraphIndex} \"{TruncateForSample(loc.MatchedText, 0, loc.MatchedText.Length)}\"";
}
