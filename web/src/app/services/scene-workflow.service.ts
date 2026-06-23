import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { apiBaseUrl } from '../core/api-config';

/** One LLM suggestion for a paragraph span (draft recommendations). */
export interface DraftRecommendationItem {
  /** Suggestion category (server-defined string). */
  kind: string;
  /** Inclusive paragraph index (0-based). */
  paragraphStart: number;
  paragraphEnd: number;
  /** Short description of the issue. */
  problem: string;
  /** Optional replacement prose. */
  replacementText?: string | null;
  /** Optional instruction if the author should rewrite manually. */
  rewriteInstruction?: string | null;
}

/** POST /api/scenes/{id}/draft/recommendations response. */
export interface DraftRecommendationResult {
  items: DraftRecommendationItem[];
}

/** GET /api/scenes/{id}/workflow-context — continuity defaults for the workflow form. */
export interface SceneWorkflowContext {
  /** False when this is the first scene in story order. */
  hasPreviousScene: boolean;
  /** Serialized state table from the prior scene’s end (continuity). */
  previousSceneEndStateJson: string | null;
  /** Suggested POV when the scene has none set. */
  defaultNarrativePerspective: string | null;
  /** Suggested tense when the scene has none set. */
  defaultNarrativeTense: string | null;
}

/** POST /api/scenes/{id}/beginning-state/derive response. */
export interface DeriveBeginningStateResult {
  beginningStateJson: string;
  derivedFromPreviousScene: boolean;
}

/** POST /api/scenes/{id}/beginning-state/convert-from-prose response. */
export interface ConvertBeginningStateFromProseResult {
  beginningStateJson: string;
}

@Injectable({ providedIn: 'root' })
export class SceneWorkflowService {
  private readonly http = inject(HttpClient);

  patchScene(
    sceneId: string,
    body: Partial<{
      title: string;
      synopsis: string;
      instructions: string;
      expectedEndStateNotes: string | null;
      narrativePerspective: string | null;
      narrativeTense: string | null;
      beginningStateJson: string | null;
      beginningStateProse: string | null;
      latestDraftText: string;
      pendingPostStateJson: string | null;
      clearPendingPostState: boolean;
      generationRunId: string;
      finalDraftText: string;
    }>
  ) {
    return this.http.patch(`${apiBaseUrl}/api/scenes/${sceneId}`, body);
  }

  getWorkflowContext(sceneId: string) {
    return this.http.get<SceneWorkflowContext>(`${apiBaseUrl}/api/scenes/${sceneId}/workflow-context`);
  }

  /** LLM-derive beginning state JSON, persist on the scene, and return it. */
  deriveBeginningState(sceneId: string) {
    return this.http.post<DeriveBeginningStateResult>(`${apiBaseUrl}/api/scenes/${sceneId}/beginning-state/derive`, {});
  }

  /** LLM-convert plain-language beginning-state prose into schema JSON and persist on the scene. */
  convertBeginningStateFromProse(sceneId: string, prose: string) {
    return this.http.post<ConvertBeginningStateFromProseResult>(
      `${apiBaseUrl}/api/scenes/${sceneId}/beginning-state/convert-from-prose`,
      { prose }
    );
  }

  /** Remove finalized manuscript and approved end-state so the scene can be rewritten. */
  clearSceneManuscript(sceneId: string) {
    return this.http.post(`${apiBaseUrl}/api/scenes/${sceneId}/manuscript/clear`, {});
  }

  suggestWorldElements(bookId: string, synopsis: string) {
    return this.http.post<{ elementIds: string[] }>(
      `${apiBaseUrl}/api/books/${bookId}/scene-synopsis/suggest-world-elements`,
      { synopsis }
    );
  }

  patchChapter(chapterId: string, body: { isComplete?: boolean }) {
    return this.http.patch(`${apiBaseUrl}/api/chapters/${chapterId}`, body);
  }

  /** On-demand LLM analysis; proposals are not applied server-side. */
  getDraftRecommendations(sceneId: string, draftText: string) {
    return this.http.post<DraftRecommendationResult>(`${apiBaseUrl}/api/scenes/${sceneId}/draft/recommendations`, {
      draftText
    });
  }
}
