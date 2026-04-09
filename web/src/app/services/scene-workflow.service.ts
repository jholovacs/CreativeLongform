import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { apiBaseUrl } from '../core/api-config';

export interface SceneWorkflowContext {
  hasPreviousScene: boolean;
  previousSceneEndStateJson: string | null;
  defaultNarrativePerspective: string | null;
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
}
