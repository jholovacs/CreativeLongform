import { formatJsonPretty } from './json-format';

/** Pretty-print helper for state JSON fields in the UI. */
describe('formatJsonPretty', () => {
  /**
   * System under test: {@link formatJsonPretty}
   * Test case: Valid compact JSON object string.
   * Expected result: Indented multi-line string with 2-space indent.
   * Why it's important: Readable state tables reduce author errors when editing JSON in textareas.
   */
  it('formats valid JSON with indentation', () => {
    expect(formatJsonPretty('{"a":1}')).toBe('{\n  "a": 1\n}');
  });

  /**
   * System under test: {@link formatJsonPretty}
   * Test case: `null`, `undefined`, whitespace-only input.
   * Expected result: Empty string (no throw).
   * Why it's important: Optional fields should not crash the template when empty.
   */
  it('returns empty for nullish or whitespace', () => {
    expect(formatJsonPretty(null)).toBe('');
    expect(formatJsonPretty(undefined)).toBe('');
    expect(formatJsonPretty('  \n  ')).toBe('');
  });

  /**
   * System under test: {@link formatJsonPretty}
   * Test case: Non-JSON or truncated JSON.
   * Expected result: Original text returned unchanged.
   * Why it's important: Authors may paste partial JSON; destructive reformatting would hide typos or lose data.
   */
  it('returns original text when not valid JSON', () => {
    expect(formatJsonPretty('not json')).toBe('not json');
    expect(formatJsonPretty(' { incomplete')).toBe(' { incomplete');
  });
});
