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
