/** Matches server AgenticEditLoop paragraph splitting (blank-line separated blocks). */

export function splitParagraphs(text: string): string[] {
  const t = text.replace(/\r\n/g, '\n');
  const parts = t.split(/\n\n/);
  const list: string[] = [];
  for (const p of parts) {
    const s = p.trim();
    if (s.length > 0) list.push(s);
  }
  if (list.length === 0 && text.trim()) list.push(text.trim());
  return list;
}

export function joinParagraphs(paragraphs: string[]): string {
  return paragraphs.join('\n\n');
}

/** Inclusive paragraph indices; replacement may expand to multiple paragraphs via blank lines. */
export function applyParagraphReplace(draft: string, start: number, end: number, replacement: string): string {
  const paras = splitParagraphs(draft);
  if (start < 0 || end < start || end >= paras.length) return draft;
  const newParas = splitParagraphs(replacement);
  if (newParas.length === 0) return draft;
  paras.splice(start, end - start + 1, ...newParas);
  return joinParagraphs(paras);
}

/**
 * Maps a normalized (\n-only) index into `draft` to a UTF-16 index suitable for textarea selection.
 * `normIndex` may be `0..t.length` (end maps to end of draft).
 */
function normIndexToDraftIndex(draft: string, normIndex: number): number {
  const t = draft.replace(/\r\n/g, '\n');
  if (normIndex <= 0) return 0;
  if (normIndex >= t.length) return draft.length;
  let d = 0;
  let n = 0;
  while (n < normIndex && d < draft.length) {
    if (draft[d] === '\r' && draft[d + 1] === '\n') {
      d += 2;
      n++;
    } else {
      d++;
      n++;
    }
  }
  return d;
}

/**
 * Paragraph spans in normalized text: `end` is exclusive (same convention as textarea selectionEnd).
 * Order and count match {@link splitParagraphs}.
 */
function paragraphRangesInNormalizedText(draft: string): { start: number; end: number }[] {
  const t = draft.replace(/\r\n/g, '\n');
  const parts = t.split(/\n\n/);
  const ranges: { start: number; end: number }[] = [];
  let offset = 0;
  for (let i = 0; i < parts.length; i++) {
    const part = parts[i];
    const partStart = offset;
    offset += part.length;
    if (i < parts.length - 1) offset += 2;
    const trimmed = part.trim();
    if (trimmed.length === 0) continue;
    const lead = part.indexOf(trimmed);
    const start = partStart + lead;
    const end = start + trimmed.length;
    ranges.push({ start, end });
  }
  if (ranges.length === 0 && draft.trim()) {
    let ts = 0;
    while (ts < t.length && /\s/.test(t[ts])) ts++;
    let te = t.length;
    while (te > ts && /\s/.test(t[te - 1])) te--;
    ranges.push({ start: ts, end: te });
  }
  return ranges;
}

/**
 * UTF-16 selection range in `draft` for inclusive paragraph indices (same as recommendation / applyParagraphReplace).
 * `end` is exclusive for `setSelectionRange`.
 */
export function getParagraphSelectionRangeInDraft(
  draft: string,
  paragraphStart: number,
  paragraphEnd: number
): { start: number; end: number } | null {
  const ranges = paragraphRangesInNormalizedText(draft);
  if (paragraphStart < 0 || paragraphEnd < paragraphStart || paragraphEnd >= ranges.length) {
    return null;
  }
  const first = ranges[paragraphStart];
  const last = ranges[paragraphEnd];
  return {
    start: normIndexToDraftIndex(draft, first.start),
    end: normIndexToDraftIndex(draft, last.end)
  };
}

/** One paragraph before / problem span / one paragraph after (same indexing as {@link splitParagraphs}). */
export function getParagraphNeighborhood(
  draft: string,
  paragraphStart: number,
  paragraphEnd: number
): { beforeContext: string; problemSpan: string; afterContext: string } | null {
  const paras = splitParagraphs(draft);
  if (paragraphStart < 0 || paragraphEnd < paragraphStart || paragraphEnd >= paras.length) {
    return null;
  }
  const beforeContext = paragraphStart > 0 ? paras[paragraphStart - 1] : '';
  const problemSpan = paras.slice(paragraphStart, paragraphEnd + 1).join('\n\n');
  const afterContext = paragraphEnd < paras.length - 1 ? paras[paragraphEnd + 1] : '';
  return { beforeContext, problemSpan, afterContext };
}
