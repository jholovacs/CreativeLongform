import { getParagraphNeighborhood, getParagraphSelectionRangeInDraft, splitParagraphs } from './draft-paragraph';

describe('getParagraphSelectionRangeInDraft', () => {
  it('selects a single paragraph span', () => {
    const draft = 'First para.\n\nSecond para.\n\nThird.';
    const r = getParagraphSelectionRangeInDraft(draft, 1, 1);
    expect(r).not.toBeNull();
    expect(draft.slice(r!.start, r!.end)).toBe('Second para.');
    expect(splitParagraphs(draft).length).toBe(3);
  });

  it('selects a multi-paragraph range inclusive', () => {
    const draft = 'A\n\nB\n\nC';
    const r = getParagraphSelectionRangeInDraft(draft, 0, 1);
    expect(r).not.toBeNull();
    expect(draft.slice(r!.start, r!.end)).toBe('A\n\nB');
  });

  it('maps CRLF draft indices for textarea', () => {
    const draft = 'Line one\r\n\r\nLine two';
    const r = getParagraphSelectionRangeInDraft(draft, 1, 1);
    expect(r).not.toBeNull();
    expect(draft.slice(r!.start, r!.end)).toBe('Line two');
  });
});

describe('getParagraphNeighborhood', () => {
  it('returns one paragraph before and after the problem span', () => {
    const draft = 'A\n\nB\n\nC\n\nD';
    const n = getParagraphNeighborhood(draft, 1, 1);
    expect(n).toEqual({
      beforeContext: 'A',
      problemSpan: 'B',
      afterContext: 'C'
    });
  });

  it('returns empty before when problem is first paragraph', () => {
    const draft = 'Only\n\nSecond';
    const n = getParagraphNeighborhood(draft, 0, 0);
    expect(n).toEqual({ beforeContext: '', problemSpan: 'Only', afterContext: 'Second' });
  });
});
