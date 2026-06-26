import { GenerationProgressPayload } from '../services/generation.service';

export type GenerationLogKind = 'phase' | 'llm' | 'agent' | 'repair' | 'run' | 'other';

/** Local-only log rows that must not replace the sticky pipeline status line. */
export const GENERATION_LOCAL_LABEL_IGNORE_STEPS = new Set(['hub', 'run id']);

export function shouldLocalEventUpdateNowLabel(step: string | null | undefined): boolean {
  if (!step) {
    return true;
  }
  return !GENERATION_LOCAL_LABEL_IGNORE_STEPS.has(step);
}

export interface GenerationLogEntry {
  id: string;
  at: Date;
  kind: GenerationLogKind;
  eventName: string;
  title: string;
  detail: string;
  elapsedMs: number | null;
  stepDurationMs: number | null;
  llmCallId: string | null;
}

/** Minimal log row shape for sticky-label resolution (component entries may carry extra fields). */
export type GenerationLogLabelSource = Pick<GenerationLogEntry, 'eventName' | 'detail' | 'title'>;

export function nextGenerationLogId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`;
}

export function formatEventBadgeLabel(name: string): string {
  if (!name) return '';
  return name
    .replace(/_/g, ' ')
    .replace(/([a-z\d])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .trim();
}

export function splitDraftParagraphs(text: string): string[] {
  return text
    .replace(/\r\n/g, '\n')
    .split(/\n\n+/)
    .map((p) => p.trim())
    .filter((p) => p.length > 0);
}

export function extractAgentNarrativeHeadline(detail: string): string | null {
  if (!detail) {
    return null;
  }
  const line = detail.split('\n').find((l) => l.trim().length > 0)?.trim() ?? '';
  if (!line || line.startsWith('Action JSON') || line.startsWith('Tool response') || line.startsWith('Turn ')) {
    return null;
  }
  if (line.startsWith('Agent concluded:') || line.startsWith('Next step:')) {
    return line;
  }
  if (
    /^(Agent|Writer|Editor|Corrector|That step|Replaced|Replace |Compliance|Search |Script |Writer rewrite|Editor touch|Corrector fixes)/i.test(
      line
    )
  ) {
    return line;
  }
  const delegationWait = line.match(/^(Writer|Editor|Corrector) is reworking .+?(?=:|$)/);
  if (delegationWait) {
    return delegationWait[0].replace(/:$/, '').trim();
  }
  return null;
}

export function generationLogTitle(eventName: string, p: GenerationProgressPayload, stepLabel: string): string {
  const detail = (p.detail ?? '').trim();
  const narrative = extractAgentNarrativeHeadline(detail);
  if (narrative) {
    return narrative;
  }

  const agentTool = (p.step ?? '').replace(/_/g, ' ').trim();
  switch (eventName) {
    case 'LlmRoundtrip':
    case 'LlmStarted':
      if (p.step === 'AgentEdit') {
        const agentLine = extractAgentNarrativeHeadline(detail);
        if (agentLine) {
          return agentLine;
        }
      }
      return stepLabel.includes('is reworking') ? stepLabel : `LLM · ${stepLabel}`;
    case 'RunStarted':
      return 'Pipeline started';
    case 'AgentEditStatus':
      return detail.split('\n')[0]?.trim() || 'Agent';
    case 'WorkingDocumentUpdated':
      return (p.step ?? '').trim() || 'Draft updated';
    case 'AgentEditAction':
      return agentTool ? `Agent → ${agentTool}` : 'Agent action';
    case 'AgentEditResult':
      return agentTool ? `Agent ← ${agentTool}` : 'Agent result';
    case 'AgentEditTurn':
      return 'Agent edit';
    case 'RepairDraftApplied':
      return 'Repair — draft updated';
    case 'DraftReviewNote':
      return 'Compliance / quality note';
    case 'Local':
      return 'Status';
    default:
      return stepLabel;
  }
}

export function mapEventToKind(eventName: string): GenerationLogKind {
  switch (eventName) {
    case 'LlmStarted':
    case 'LlmRoundtrip':
      return 'llm';
    case 'AgentEditTurn':
    case 'AgentEditAction':
    case 'AgentEditResult':
    case 'AgentEditStatus':
      return 'agent';
    case 'RepairAttempt':
    case 'RepairDraftApplied':
      return 'repair';
    case 'RunStarted':
    case 'RunFinished':
    case 'StepStarted':
      return 'run';
    case 'WorkingDocumentUpdated':
      return 'phase';
    default:
      return 'other';
  }
}

export function generationNowLabelForEvent(eventName: string, p: GenerationProgressPayload): string | null {
  const detail = (p.detail ?? '').trim();
  switch (eventName) {
    case 'LlmStarted':
      return extractAgentNarrativeHeadline(detail) ?? (detail || `Waiting on model (${p.step ?? 'LLM'})…`);
    case 'LlmRoundtrip':
      return extractAgentNarrativeHeadline(detail) ?? 'Processing model response…';
    case 'StepStarted':
    case 'RunStarted':
    case 'AgentEditTurn':
    case 'AgentEditAction':
    case 'AgentEditResult':
    case 'AgentEditStatus':
      return extractAgentNarrativeHeadline(detail) ?? (detail || (p.step ?? 'Working…'));
    case 'WorkingDocumentUpdated':
      return (p.step ?? '').trim() || 'Draft updated';
    case 'Local':
      if (detail && shouldLocalEventUpdateNowLabel(p.step)) {
        return detail;
      }
      return null;
    default:
      return detail ? extractAgentNarrativeHeadline(detail) ?? detail : null;
  }
}

export function pickGenerationNowLabelFromLog(entries: readonly GenerationLogLabelSource[]): string | null {
  const latest = entries.find((e) => e.eventName !== 'Local' && e.eventName !== 'LlmStreamChunk');
  if (!latest) {
    return null;
  }
  const fromDetail = extractAgentNarrativeHeadline(latest.detail) ?? latest.detail.trim();
  if (fromDetail) {
    return fromDetail;
  }
  return latest.title.trim() || null;
}

/**
 * After SignalR JoinRun, replayed server events may already be in the log while the hub
 * adds a local "Live events connected." row — restore the sticky label from server events.
 */
export function resolveGenerationNowLabelAfterHubConnect(
  entries: readonly GenerationLogLabelSource[],
  currentLabel: string | null
): string | null {
  return pickGenerationNowLabelFromLog(entries) ?? currentLabel;
}

export function buildGenerationLogEntry(eventName: string, p: GenerationProgressPayload): GenerationLogEntry {
  const stepLabel = (p.step ?? '').replace(/_/g, ' ').trim() || eventName;
  return {
    id: nextGenerationLogId(),
    at: new Date(),
    kind: mapEventToKind(eventName),
    eventName,
    title: generationLogTitle(eventName, p, stepLabel),
    detail: (p.detail ?? '').trim(),
    elapsedMs: p.elapsedMs ?? null,
    stepDurationMs: p.stepDurationMs ?? null,
    llmCallId: p.llmCallId ?? null
  };
}

export interface WorkingDocumentDiff {
  paragraphs: string[];
  changedIndices: Set<number>;
  revision: number;
  changeSummary: string;
}

export function diffWorkingDocument(
  prevParagraphs: string[],
  text: string,
  p: GenerationProgressPayload,
  currentRevision = 0
): WorkingDocumentDiff | null {
  const trimmed = (text ?? '').trim();
  if (!trimmed) {
    return null;
  }

  const nextParagraphs = splitDraftParagraphs(trimmed);
  const changed = new Set<number>();
  const maxLen = Math.max(prevParagraphs.length, nextParagraphs.length);
  for (let i = 0; i < maxLen; i++) {
    if ((prevParagraphs[i] ?? '') !== (nextParagraphs[i] ?? '')) {
      changed.add(i);
    }
  }

  return {
    paragraphs: nextParagraphs,
    changedIndices: changed,
    revision: p.documentRevision ?? currentRevision + 1,
    changeSummary: (p.step ?? '').trim() || 'Draft updated'
  };
}

export function formatDuration(ms: number | null | undefined): string {
  if (ms == null || ms < 0) return '';
  if (ms < 1000) return `${Math.round(ms)} ms`;
  const s = ms / 1000;
  if (s < 60) return `${s.toFixed(1)} s`;
  const m = Math.floor(s / 60);
  const rem = Math.round(s % 60);
  return `${m}m ${rem}s`;
}
