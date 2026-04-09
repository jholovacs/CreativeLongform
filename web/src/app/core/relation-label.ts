/** Display helper: snake_case → "Title Case Words" (matches server HumanizeRelationLabel). */
export function formatRelationLabel(raw: string | null | undefined): string {
  if (raw == null || !String(raw).trim()) return '';
  const s = String(raw).trim();
  if (!s.includes('_')) return s;
  return s
    .split('_')
    .filter(Boolean)
    .map((w) => w.charAt(0).toUpperCase() + w.slice(1).toLowerCase())
    .join(' ');
}
