import {
  buildGenerationLogEntry,
  generationNowLabelForEvent,
  pickGenerationNowLabelFromLog,
  resolveGenerationNowLabelAfterHubConnect,
  shouldLocalEventUpdateNowLabel
} from './generation-progress-log';
import { GenerationProgressPayload } from '../services/generation.service';

describe('generation progress sticky label (regression)', () => {
  const draftLlmStarted: GenerationProgressPayload = {
    runId: 'run-1',
    step: 'Draft',
    detail: 'Draft: streaming from model «llama3»…',
    llmCallId: 'call-1'
  };

  const hubConnectedLocal: GenerationProgressPayload = {
    runId: 'run-1',
    step: 'hub',
    detail: 'Live events connected.'
  };

  /**
   * Regression: hub connect pushed Local "Live events connected." as the sticky label and
   * masked replayed StepStarted/LlmStarted events — the dialog looked hung during long drafts.
   */
  it('does not let hub-connected local row overwrite an in-flight LlmStarted label', () => {
    const fromLlm = generationNowLabelForEvent('LlmStarted', draftLlmStarted);
    expect(fromLlm).toContain('Draft: streaming');

    const fromHub = generationNowLabelForEvent('Local', hubConnectedLocal);
    expect(fromHub).toBeNull();

    let label = fromLlm;
    if (fromHub !== null) {
      label = fromHub;
    }
    expect(label).toContain('Draft: streaming');
    expect(label).not.toBe('Live events connected.');
  });

  it('does not let run-id local row overwrite the sticky label', () => {
    expect(shouldLocalEventUpdateNowLabel('hub')).toBeFalse();
    expect(shouldLocalEventUpdateNowLabel('run id')).toBeFalse();
    expect(shouldLocalEventUpdateNowLabel('start')).toBeTrue();

    const fromRunId = generationNowLabelForEvent('Local', {
      runId: 'run-1',
      step: 'run id',
      detail: 'Generation run run-1 started. Connecting to live events…'
    });
    expect(fromRunId).toBeNull();
  });

  it('resolveGenerationNowLabelAfterHubConnect restores server status from the log', () => {
    const entries = [
      buildGenerationLogEntry('Local', hubConnectedLocal),
      buildGenerationLogEntry('LlmStarted', draftLlmStarted)
    ];

    const resolved = resolveGenerationNowLabelAfterHubConnect(entries, 'Live events connected.');
    expect(resolved).toContain('Draft: streaming');
    expect(resolved).not.toBe('Live events connected.');
  });

  it('pickGenerationNowLabelFromLog ignores local and stream-chunk rows', () => {
    const entries = [
      buildGenerationLogEntry('Local', hubConnectedLocal),
      buildGenerationLogEntry('LlmStreamChunk', {
        runId: 'run-1',
        step: 'Draft',
        detail: 'token '
      }),
      buildGenerationLogEntry('StepStarted', {
        runId: 'run-1',
        step: 'PreState',
        detail: 'Pre-state: resolving beginning narrative state (author JSON, prior scene, or LLM).'
      })
    ];

    const label = pickGenerationNowLabelFromLog(entries);
    expect(label).toContain('Pre-state');
  });

  /**
   * Regression: LlmStarted on non-agent steps was omitted from the log, so only Local rows
   * appeared while the draft model ran for minutes.
   */
  it('records LlmStarted for Draft in the generation log', () => {
    const entry = buildGenerationLogEntry('LlmStarted', draftLlmStarted);
    expect(entry.eventName).toBe('LlmStarted');
    expect(entry.kind).toBe('llm');
    expect(entry.title).toContain('Draft');
    expect(entry.detail).toContain('streaming');
  });
});
