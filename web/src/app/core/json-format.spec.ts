import { formatJsonPretty } from './json-format';

describe('formatJsonPretty', () => {
  it('formats valid JSON with indentation', () => {
    expect(formatJsonPretty('{"a":1}')).toBe('{\n  "a": 1\n}');
  });

  it('returns empty for nullish or whitespace', () => {
    expect(formatJsonPretty(null)).toBe('');
    expect(formatJsonPretty(undefined)).toBe('');
    expect(formatJsonPretty('  \n  ')).toBe('');
  });

  it('returns original text when not valid JSON', () => {
    expect(formatJsonPretty('not json')).toBe('not json');
    expect(formatJsonPretty(' { incomplete')).toBe(' { incomplete');
  });
});
