import {
  GENERATION_RUN_STATUS,
  generationRunFinishedStep,
  isActiveGenerationRunStatus,
  isTerminalGenerationRunStatus,
  normalizeGenerationRunStatus
} from './generation-run-status';

describe('generation-run-status', () => {
  it('normalizes OData enum string Running as active', () => {
    expect(normalizeGenerationRunStatus('Running')).toBe(GENERATION_RUN_STATUS.Running);
    expect(isActiveGenerationRunStatus(normalizeGenerationRunStatus('Running'))).toBe(true);
    expect(isTerminalGenerationRunStatus(normalizeGenerationRunStatus('Running'))).toBe(false);
  });

  it('normalizes OData enum literal syntax', () => {
    const literal = "CreativeLongform.Domain.Enums.GenerationRunStatus'AwaitingUserReview'";
    expect(normalizeGenerationRunStatus(literal)).toBe(GENERATION_RUN_STATUS.AwaitingUserReview);
    expect(normalizeGenerationRunStatus("CreativeLongform.Domain.Enums.GenerationRunStatus'Running'")).toBe(
      GENERATION_RUN_STATUS.Running
    );
    expect(generationRunFinishedStep(GENERATION_RUN_STATUS.AwaitingUserReview)).toBe('AwaitingUserReview');
  });

  it('does not treat unknown status as terminal', () => {
    expect(isTerminalGenerationRunStatus(normalizeGenerationRunStatus('NotAStatus'))).toBe(false);
  });
});
