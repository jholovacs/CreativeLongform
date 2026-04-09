import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { apiBaseUrl } from '../core/api-config';
import {
  ODataCollection,
  SceneWorldElementRow,
  WorldBuildingApplyResult,
  WorldElement
} from '../models/entities';

@Injectable({ providedIn: 'root' })
export class WorldService {
  private readonly http = inject(HttpClient);

  getWorldElements(bookId: string) {
    const filter = `bookId eq guid'${bookId}'`;
    return this.http.get<ODataCollection<WorldElement>>(
      `${apiBaseUrl}/odata/WorldElements`,
      { params: new HttpParams().set('$filter', filter).set('$orderby', 'title') }
    );
  }

  getSceneWorldElementIds(sceneId: string) {
    const filter = `sceneId eq guid'${sceneId}'`;
    return this.http.get<ODataCollection<SceneWorldElementRow>>(
      `${apiBaseUrl}/odata/SceneWorldElements`,
      {
        params: new HttpParams()
          .set('$filter', filter)
          .set('$select', 'sceneId,worldElementId')
      }
    );
  }

  patchStoryProfile(
    bookId: string,
    body: {
      storyToneAndStyle?: string;
      contentStyleNotes?: string | null;
      measurementPreset?: number;
      measurementSystemJson?: string | null;
    }
  ) {
    return this.http.patch(`${apiBaseUrl}/api/books/${bookId}/story-profile`, body);
  }

  extractFromText(bookId: string, text: string) {
    return this.http.post<WorldBuildingApplyResult>(
      `${apiBaseUrl}/api/books/${bookId}/world/extract`,
      { text }
    );
  }

  generateFromPrompt(bookId: string, prompt: string) {
    return this.http.post<WorldBuildingApplyResult>(
      `${apiBaseUrl}/api/books/${bookId}/world/generate`,
      { prompt }
    );
  }

  putSceneWorldElements(sceneId: string, worldElementIds: string[]) {
    return this.http.put(`${apiBaseUrl}/api/scenes/${sceneId}/world-elements`, {
      worldElementIds
    });
  }
}
