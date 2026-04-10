import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { apiBaseUrl } from '../core/api-config';
import { SKIP_GLOBAL_ERROR_MODAL } from '../core/http-context-tokens';

export interface OllamaModelAssignmentsDto {
  writerModel: string;
  criticModel: string;
  agentModel: string;
  worldBuildingModel: string;
  preStateModel: string;
  postStateModel: string;
  dbOverriddenRoles: number[];
}

export interface OllamaPreferencesResponse {
  assignments: OllamaModelAssignmentsDto;
  installedModels: string[];
  ollamaListError: string | null;
}

export interface OllamaModelAssignmentsPatch {
  writerModel?: string | null;
  criticModel?: string | null;
  agentModel?: string | null;
  worldBuildingModel?: string | null;
  preStateModel?: string | null;
  postStateModel?: string | null;
  clearWriter?: boolean;
  clearCritic?: boolean;
  clearAgent?: boolean;
  clearWorldBuilding?: boolean;
  clearPreState?: boolean;
  clearPostState?: boolean;
}

export interface OllamaModelChangeLogDto {
  id: string;
  occurredAt: string;
  role: number;
  previousModel: string | null;
  newModel: string;
  source: string;
}

@Injectable({ providedIn: 'root' })
export class OllamaModelsService {
  private readonly http = inject(HttpClient);

  getPreferences() {
    return this.http.get<OllamaPreferencesResponse>(`${apiBaseUrl}/api/ollama/preferences`);
  }

  putPreferences(body: OllamaModelAssignmentsPatch) {
    return this.http.put<OllamaModelAssignmentsDto>(`${apiBaseUrl}/api/ollama/preferences`, body);
  }

  getChangeLog(take = 80) {
    return this.http.get<OllamaModelChangeLogDto[]>(`${apiBaseUrl}/api/ollama/change-log`, {
      params: { take: String(take) },
      context: new HttpContext().set(SKIP_GLOBAL_ERROR_MODAL, true)
    });
  }

  pullLibraryModel(model: string) {
    return this.http.post(`${apiBaseUrl}/api/ollama/pull`, { model }, { observe: 'response' });
  }

  importFromUrl(url: string, modelName: string) {
    return this.http.post(`${apiBaseUrl}/api/ollama/import-url`, { url, modelName }, { observe: 'response' });
  }
}
