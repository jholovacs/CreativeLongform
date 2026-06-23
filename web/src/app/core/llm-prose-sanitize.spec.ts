import { splitLlmThinkingFromProse } from './llm-prose-sanitize';

/** Tag name some providers use for reasoning blocks (built at runtime so tooling does not strip it). */
const REDACTED_THINKING_TAG = 'redacted_' + 'thinking';

describe('splitLlmThinkingFromProse', () => {
  it('removes redacted_thinking blocks', () => {
    const raw = `<${REDACTED_THINKING_TAG}>Plan beat.</${REDACTED_THINKING_TAG}>\n\nShe walked in.`;
    const split = splitLlmThinkingFromProse(raw);
    expect(split.prose).toBe('She walked in.');
    expect(split.thinkingNotes).toContain('Plan beat.');
  });
});
