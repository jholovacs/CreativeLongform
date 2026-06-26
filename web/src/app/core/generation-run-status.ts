/** Matches server `GenerationRunStatus` (OData returns enum member names, not integers). */
export const GENERATION_RUN_STATUS = {
  Pending: 0,
  Running: 1,
  Succeeded: 2,
  Failed: 3,
  AwaitingUserReview: 4,
  Cancelled: 5
} as const;

export type GenerationRunStatusCode = (typeof GENERATION_RUN_STATUS)[keyof typeof GENERATION_RUN_STATUS];

/** Normalize OData `status` (number or enum string) to a status code, or null when unknown. */
export function normalizeGenerationRunStatus(raw: unknown): GenerationRunStatusCode | null {
  if (typeof raw === 'number' && Number.isFinite(raw) && raw >= 0 && raw <= 5) {
    return raw as GenerationRunStatusCode;
  }
  if (typeof raw !== 'string') {
    return null;
  }
  let name = raw.trim();
  if (name.includes("'")) {
    const segments = name.split("'").filter((s) => s.length > 0);
    name = segments[segments.length - 1] ?? name;
  }
  if (name in GENERATION_RUN_STATUS) {
    return GENERATION_RUN_STATUS[name as keyof typeof GENERATION_RUN_STATUS];
  }
  return null;
}

export function isActiveGenerationRunStatus(status: GenerationRunStatusCode | null): boolean {
  return status === GENERATION_RUN_STATUS.Pending || status === GENERATION_RUN_STATUS.Running;
}

export function isTerminalGenerationRunStatus(status: GenerationRunStatusCode | null): boolean {
  return (
    status === GENERATION_RUN_STATUS.Succeeded ||
    status === GENERATION_RUN_STATUS.Failed ||
    status === GENERATION_RUN_STATUS.AwaitingUserReview ||
    status === GENERATION_RUN_STATUS.Cancelled
  );
}

export function generationRunFinishedStep(status: GenerationRunStatusCode): string {
  switch (status) {
    case GENERATION_RUN_STATUS.AwaitingUserReview:
      return 'AwaitingUserReview';
    case GENERATION_RUN_STATUS.Cancelled:
      return 'Cancelled';
    case GENERATION_RUN_STATUS.Failed:
      return 'Failed';
    default:
      return 'Succeeded';
  }
}
