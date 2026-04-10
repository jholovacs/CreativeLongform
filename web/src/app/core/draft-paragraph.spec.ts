import { getParagraphNeighborhood, getParagraphSelectionRangeInDraft, splitParagraphs } from './draft-paragraph';

/**
 * Draft textarea helpers: map logical paragraph indices to string ranges for selection and context chips.
 */
describe('getParagraphSelectionRangeInDraft', () => {
  /**
   * System under test: {@link getParagraphSelectionRangeInDraft} with {@link splitParagraphs}.
   * Test case: Three paragraphs; select index 1 only.
   * Expected result: Slice matches the second paragraph text; split count is 3.
   * Why it's important: Wrong indices break “replace this paragraph” and compliance highlights in the draft editor.
   */
  it('selects a single paragraph span', () => {
    const draft = 'First para.\n\nSecond para.\n\nThird.';
    const r = getParagraphSelectionRangeInDraft(draft, 1, 1);
    expect(r).not.toBeNull();
    expect(draft.slice(r!.start, r!.end)).toBe('Second para.');
    expect(splitParagraphs(draft).length).toBe(3);
  });

  /**
   * System under test: {@link getParagraphSelectionRangeInDraft} inclusive range.
   * Test case: Select paragraphs 0 through 1.
   * Expected result: Slice includes both paragraphs and the blank line between.
   * Why it's important: Multi-paragraph patches must map to a single textarea selection for replace operations.
   */
  it('selects a multi-paragraph range inclusive', () => {
    const draft = 'A\n\nB\n\nC';
    const r = getParagraphSelectionRangeInDraft(draft, 0, 1);
    expect(r).not.toBeNull();
    expect(draft.slice(r!.start, r!.end)).toBe('A\n\nB');
  });

  /**
   * System under test: {@link getParagraphSelectionRangeInDraft} with CRLF drafts.
   * Test case: Windows newlines in body; select second paragraph.
   * Expected result: Slice equals the visible second paragraph including CR if present in source.
   * Why it's important: Browser textareas on Windows often use CRLF; index math must stay consistent with stored draft text.
   */
  it('maps CRLF draft indices for textarea', () => {
    const draft = 'Line one\r\n\r\nLine two';
    const r = getParagraphSelectionRangeInDraft(draft, 1, 1);
    expect(r).not.toBeNull();
    expect(draft.slice(r!.start, r!.end)).toBe('Line two');
  });
});

describe('getParagraphNeighborhood', () => {
  /**
   * System under test: {@link getParagraphNeighborhood}.
   * Test case: Four paragraphs; problem span is index 1.
   * Expected result: Before = prior paragraph, problem = middle, after = following.
   * Why it's important: LLM “fix this paragraph” prompts need local context without sending the full manuscript.
   */
  it('returns one paragraph before and after the problem span', () => {
    const draft = 'A\n\nB\n\nC\n\nD';
    const n = getParagraphNeighborhood(draft, 1, 1);
    expect(n).toEqual({
      beforeContext: 'A',
      problemSpan: 'B',
      afterContext: 'C'
    });
  });

  /**
   * System under test: {@link getParagraphNeighborhood} edge at start.
   * Test case: First paragraph is the problem; second exists.
   * Expected result: `beforeContext` empty; problem and after populated.
   * Why it's important: Avoids undefined/ off-by-one when the issue is the opening paragraph.
   */
  it('returns empty before when problem is first paragraph', () => {
    const draft = 'Only\n\nSecond';
    const n = getParagraphNeighborhood(draft, 0, 0);
    expect(n).toEqual({ beforeContext: '', problemSpan: 'Only', afterContext: 'Second' });
  });
});
