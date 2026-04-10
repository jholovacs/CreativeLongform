/** Pretty-print JSON for display; invalid JSON is returned unchanged. */
export function formatJsonPretty(text: string | null | undefined): string {
  const raw = text ?? '';
  const t = raw.trim();
  if (!t) {
    return '';
  }
  try {
    return JSON.stringify(JSON.parse(t), null, 2);
  } catch {
    return raw;
  }
}
