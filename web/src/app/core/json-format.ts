/** True when JSON is missing, whitespace-only, or an empty object `{}`. */
export function isEmptyStateJson(value: unknown): boolean {
  const text = jsonFieldToText(value).trim();
  if (!text) {
    return true;
  }
  try {
    const parsed = JSON.parse(text) as unknown;
    return (
      typeof parsed === 'object' &&
      parsed !== null &&
      !Array.isArray(parsed) &&
      Object.keys(parsed as Record<string, unknown>).length === 0
    );
  } catch {
    return false;
  }
}

/** Coerce OData jsonb (string or parsed object) to a JSON text value. */
export function jsonFieldToText(value: unknown): string {
  if (value == null) {
    return '';
  }
  if (typeof value === 'string') {
    return value;
  }
  if (typeof value === 'object') {
    try {
      return JSON.stringify(value);
    } catch {
      return '';
    }
  }
  return String(value);
}

/** Pretty-print JSON for display; invalid JSON is returned unchanged. */
export function formatJsonPretty(text: string | null | undefined | unknown): string {
  if (text != null && typeof text === 'object') {
    try {
      return JSON.stringify(text, null, 2);
    } catch {
      return '';
    }
  }
  const raw = text ?? '';
  const t = String(raw).trim();
  if (!t) {
    return '';
  }
  try {
    return JSON.stringify(JSON.parse(t), null, 2);
  } catch {
    return String(raw);
  }
}
