/** Parses persisted `ComplianceEvaluation.verdictJson` by pipeline kind (server shapes). */
export interface ParsedComplianceVerdict {
  violations: string[];
  fixInstructions: string[];
  issues: string[];
  gaps: string[];
  score: number | null;
}

export function parseComplianceVerdictJson(verdictJson: string, kind: string): ParsedComplianceVerdict {
  const empty: ParsedComplianceVerdict = {
    violations: [],
    fixInstructions: [],
    issues: [],
    gaps: [],
    score: null
  };
  try {
    const o = JSON.parse(verdictJson) as Record<string, unknown>;
    const fixInstructions = Array.isArray(o['fixInstructions'])
      ? (o['fixInstructions'] as unknown[]).map((x) => String(x))
      : [];

    if (kind === 'Quality') {
      const rawScore = o['score'];
      const score =
        typeof rawScore === 'number' && !Number.isNaN(rawScore) ? rawScore : null;
      const issues = Array.isArray(o['issues']) ? (o['issues'] as unknown[]).map((x) => String(x)) : [];
      return { ...empty, score, issues, fixInstructions };
    }

    if (kind === 'Transition') {
      const gaps = Array.isArray(o['gaps']) ? (o['gaps'] as unknown[]).map((x) => String(x)) : [];
      return { ...empty, gaps };
    }

    const violations = Array.isArray(o['violations'])
      ? (o['violations'] as unknown[]).map((x) => String(x))
      : [];
    return { ...empty, violations, fixInstructions };
  } catch {
    return empty;
  }
}
