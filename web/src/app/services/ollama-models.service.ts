import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { apiBaseUrl } from '../core/api-config';
import { SKIP_GLOBAL_ERROR_MODAL } from '../core/http-context-tokens';

/** Active Ollama model name per pipeline role (matches API GET/PUT preferences body). */
export interface OllamaModelAssignmentsDto {
  /** Main prose / draft generation model. */
  writerModel: string;
  /** Quality / compliance pass model. */
  criticModel: string;
  /** Agentic edit loop model. */
  agentModel: string;
  /** World-building extraction and glossary passes. */
  worldBuildingModel: string;
  /** Beginning-state table model. */
  preStateModel: string;
  /** Post-scene state table model. */
  postStateModel: string;
  /** Bitflags of roles forced in DB (server-defined; UI may show override badges). */
  dbOverriddenRoles: number[];
}

/** GET /api/ollama/preferences — assignments plus installed Ollama tags and list errors. */
export interface OllamaPreferencesResponse {
  assignments: OllamaModelAssignmentsDto;
  /** Model names reported by `ollama list` on the host. */
  installedModels: string[];
  /** Non-null when listing local models failed (still may show saved assignments). */
  ollamaListError: string | null;
}

/** Partial PUT body: set a role or clear it back to default. */
export interface OllamaModelAssignmentsPatch {
  writerModel?: string | null;
  criticModel?: string | null;
  agentModel?: string | null;
  worldBuildingModel?: string | null;
  preStateModel?: string | null;
  postStateModel?: string | null;
  /** When true, server clears writer override. */
  clearWriter?: boolean;
  clearCritic?: boolean;
  clearAgent?: boolean;
  clearWorldBuilding?: boolean;
  clearPreState?: boolean;
  clearPostState?: boolean;
}

/** One row from GET /api/ollama/change-log (audit of model assignments). */
export interface OllamaModelChangeLogDto {
  id: string;
  occurredAt: string;
  /** Pipeline role enum as integer (matches server). */
  role: number;
  previousModel: string | null;
  newModel: string;
  /** e.g. user UI vs. API. */
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
