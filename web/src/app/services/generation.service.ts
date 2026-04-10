import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { apiBaseUrl } from '../core/api-config';

/** Payload from SignalR generation hub (camelCase from server). */
export interface GenerationProgressPayload {
  /** Generation run id (same as OData `GenerationRuns`). */
  runId: string;
  /** Pipeline step name or phase label. */
  step: string | null;
  /** Human-readable detail for the log UI. */
  detail: string | null;
  /** Milliseconds since the pipeline run started (server wall clock). */
  elapsedMs?: number | null;
  /** Duration of this operation (e.g. one LLM round-trip or agent turn). */
  stepDurationMs?: number | null;
  /** When set, load full request/response via `GET /odata/LlmCalls` (filter by id). */
  llmCallId?: string | null;
}

/** Response from POST /api/scenes/{id}/generation/correct (camelCase JSON). */
export interface CorrectDraftResponse {
  /** Full draft after correction pass. */
  correctedDraftText: string;
  /** Updated post-scene state if the model returned one. */
  pendingPostStateJson: string | null;
}

/** Server defaults for generation UI (GET /api/settings/generation). */
export interface GenerationDefaultsDto {
  /** At or above: automated quality repair skipped. */
  qualityAcceptMinScore: number;
  /** Minimum score to pass; between this and accept: pass with annotations only. */
  qualityReviewOnlyMinScore: number;
}

@Injectable({ providedIn: 'root' })
export class GenerationService {
  private readonly http = inject(HttpClient);

  getGenerationDefaults() {
    return this.http.get<GenerationDefaultsDto>(`${apiBaseUrl}/api/settings/generation`);
  }

  startGeneration(
    sceneId: string,
    opts?: {
      idempotencyKey?: string | null;
      stopAfterDraft?: boolean;
      minWordsOverride?: number | null;
      /** Upper bound for the draft length band in prompts (optional; server derives from min when omitted). */
      maxWordsOverride?: number | null;
      /** Skips LLM prose quality loop; compliance still runs. */
      skipQualityGate?: boolean;
      /** 0–100; at or above: no automated quality repair. Default from server config. */
      qualityAcceptMinScore?: number | null;
      /** 0–100; minimum to pass; between this and accept: pass with annotations only. */
      qualityReviewOnlyMinScore?: number | null;
    }
  ) {
    return this.http.post<{ id: string }>(`${apiBaseUrl}/api/scenes/${sceneId}/generation`, {
      idempotencyKey: opts?.idempotencyKey ?? null,
      stopAfterDraft: opts?.stopAfterDraft ?? false,
      minWordsOverride: opts?.minWordsOverride ?? null,
      maxWordsOverride: opts?.maxWordsOverride ?? null,
      skipQualityGate: opts?.skipQualityGate ?? false,
      qualityAcceptMinScore: opts?.qualityAcceptMinScore ?? null,
      qualityReviewOnlyMinScore: opts?.qualityReviewOnlyMinScore ?? null
    });
  }

  finalizeGeneration(
    sceneId: string,
    body: {
      generationRunId: string;
      acceptedDraftText?: string | null;
      approvedStateTableJson?: string | null;
    }
  ) {
    return this.http.post<{ stateTableJson: string; nextSceneId: string | null }>(
      `${apiBaseUrl}/api/scenes/${sceneId}/generation/finalize`,
      body
    );
  }

  correctDraft(
    sceneId: string,
    body: {
      generationRunId: string;
      instruction: string;
      /** Full editor draft; sent for context and so selection indices match the textarea. */
      currentDraftText?: string | null;
      /** Inclusive start, UTF-16 (same as textarea.selectionStart). */
      selectionStart?: number | null;
      /** Exclusive end, UTF-16 (same as textarea.selectionEnd). */
      selectionEnd?: number | null;
    }
  ) {
    return this.http.post<CorrectDraftResponse>(`${apiBaseUrl}/api/scenes/${sceneId}/generation/correct`, body);
  }

  cancelGeneration(sceneId: string, generationRunId: string) {
    return this.http.post<void>(`${apiBaseUrl}/api/scenes/${sceneId}/generation/${generationRunId}/cancel`, {});
  }

  connectToRun(
    runId: string,
    handlers: {
      /** All pipeline events except RunFinished (use onFinished). */
      onProgress?: (eventName: string, payload: GenerationProgressPayload) => void;
      onFinished?: (payload: GenerationProgressPayload) => void;
    }
  ): HubConnection {
    const connection = new HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/generation`, { withCredentials: false })
      .configureLogging(LogLevel.Information)
      .build();

    const emit = (eventName: string, payload: GenerationProgressPayload) => handlers.onProgress?.(eventName, payload);

    connection.on('StepStarted', (payload: GenerationProgressPayload) => emit('StepStarted', payload));
    connection.on('RunStarted', (payload: GenerationProgressPayload) => emit('RunStarted', payload));
    connection.on('AgentEditTurn', (payload: GenerationProgressPayload) => emit('AgentEditTurn', payload));
    connection.on('RepairAttempt', (payload: GenerationProgressPayload) => emit('RepairAttempt', payload));
    connection.on('RepairDraftApplied', (payload: GenerationProgressPayload) => emit('RepairDraftApplied', payload));
    connection.on('DraftReviewNote', (payload: GenerationProgressPayload) => emit('DraftReviewNote', payload));
    connection.on('LlmRoundtrip', (payload: GenerationProgressPayload) => emit('LlmRoundtrip', payload));
    connection.on('RunFinished', (payload: GenerationProgressPayload) => handlers.onFinished?.(payload));

    void connection
      .start()
      .then(() => connection.invoke('JoinRun', runId))
      .catch((err) => console.error('SignalR connection failed', err));

    return connection;
  }
}
