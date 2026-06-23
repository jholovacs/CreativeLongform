/** Separates model thinking XML blocks from prose (mirrors server LlmProseSanitizer). */
export interface LlmProseSplit {
  prose: string;
  thinkingNotes: string | null;
}

function collapseExtraBlankLines(text: string): string {
  const lines = text.replace(/\r\n/g, '\n').split('\n');
  const out: string[] = [];
  let blankRun = 0;
  for (const line of lines) {
    if (!line.trim()) {
      blankRun++;
      if (blankRun <= 2) {
        out.push('');
      }
      continue;
    }
    blankRun = 0;
    out.push(line.trimEnd());
  }
  return out.join('\n').trim();
}

function extractBlocks(text: string, tag: string, thinkingParts: string[]): string {
  const open = `<${tag}`;
  const close = `</${tag}>`;
  let sb = '';
  let i = 0;
  while (i < text.length) {
    const start = text.toLowerCase().indexOf(open.toLowerCase(), i);
    if (start < 0) {
      sb += text.slice(i);
      break;
    }

    sb += text.slice(i, start);
    const openEnd = text.indexOf('>', start);
    if (openEnd < 0) {
      sb += text.slice(start);
      break;
    }

    const closeStart = text.toLowerCase().indexOf(close.toLowerCase(), openEnd + 1);
    if (closeStart < 0) {
      sb += text.slice(start);
      break;
    }

    const inner = text.slice(openEnd + 1, closeStart).trim();
    if (inner) {
      thinkingParts.push(inner);
    }

    i = closeStart + close.length;
  }
  return sb;
}

/** Removes thinking blocks from prose; returns inner reasoning text as notes. */
export function splitLlmThinkingFromProse(raw: string | null | undefined): LlmProseSplit {
  if (!raw?.trim()) {
    return { prose: '', thinkingNotes: null };
  }

  const thinkingParts: string[] = [];
  let prose = raw;
  prose = extractBlocks(prose, 'redacted_thinking', thinkingParts);
  prose = extractBlocks(prose, 'thinking', thinkingParts);
  prose = collapseExtraBlankLines(prose.trim());

  if (thinkingParts.length === 0) {
    return { prose, thinkingNotes: null };
  }
  return { prose, thinkingNotes: thinkingParts.join('\n\n---\n\n').trim() };
}
