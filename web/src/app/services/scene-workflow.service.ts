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
