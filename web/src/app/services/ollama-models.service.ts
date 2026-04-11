import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { apiBaseUrl } from '../core/api-config';
import { SKIP_GLOBAL_ERROR_MODAL } from '../core/http-context-tokens';

/** One NDJSON line from Ollama <code>POST /api/pull</code> with <code>stream: true</code>. */
export interface OllamaPullStreamLine {
  status?: string;
  digest?: string;
  total?: number;
  completed?: number;
  error?: string;
}

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

/** One installed model from `GET /api/tags` (plus VRAM when loaded). */
export interface OllamaInstalledModelDto {
  name: string;
  /** On-disk footprint (bytes). */
  sizeBytes: number;
  /** e.g. `7.6B` from Ollama. */
  parameterSize: string | null;
  quantizationLevel: string | null;
  /** VRAM when loaded (`GET /api/ps`); null if not in memory. */
  vramBytes: number | null;
}

/** Free/total disk for the configured path (API host filesystem). */
export interface OllamaDiskSpaceDto {
  pathChecked: string;
  bytesFree: number;
  bytesTotal: number;
}

/** GET /api/ollama/preferences — assignments plus installed Ollama tags and list errors. */
export interface OllamaPreferencesResponse {
  assignments: OllamaModelAssignmentsDto;
  installedModels: OllamaInstalledModelDto[];
  /** Non-null when listing local models failed (still may show saved assignments). */
  ollamaListError: string | null;
  /** Present when <code>Ollama:DiskSpaceCheckPath</code> or <code>ImportStagingDirectory</code> is set and readable. */
  diskSpace?: OllamaDiskSpaceDto | null;
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

  /** Server clamps `take` to 1–500 (newest first). */
  getChangeLog(take = 500) {
    return this.http.get<OllamaModelChangeLogDto[]>(`${apiBaseUrl}/api/ollama/change-log`, {
      params: { take: String(take) },
      context: new HttpContext().set(SKIP_GLOBAL_ERROR_MODAL, true)
    });
  }

  pullLibraryModel(model: string) {
    return this.http.post(`${apiBaseUrl}/api/ollama/pull`, { model }, { observe: 'response' });
  }

  /**
   * Streams a library pull through our API (proxies Ollama NDJSON). Updates UI from each line (progress, status text).
   * Uses <code>fetch</code> so the response body can be read incrementally (Angular HttpClient buffers the full body).
   */
  pullLibraryModelStream(
    model: string,
    onLine: (line: OllamaPullStreamLine) => void,
    signal?: AbortSignal
  ): Promise<void> {
    const url = `${apiBaseUrl}/api/ollama/pull/stream`;
    return (async () => {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/x-ndjson' },
        body: JSON.stringify({ model }),
        credentials: 'include',
        signal
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text.trim() || `Pull failed (${res.status})`);
      }
      const reader = res.body?.getReader();
      if (!reader) {
        throw new Error('No response body from pull stream.');
      }
      const dec = new TextDecoder();
      let buf = '';
      for (;;) {
        const { done, value } = await reader.read();
        if (value) {
          buf += dec.decode(value, { stream: true });
        }
        const lines = buf.split('\n');
        buf = lines.pop() ?? '';
        for (const line of lines) {
          const t = line.trim();
          if (!t) continue;
          let obj: OllamaPullStreamLine;
          try {
            obj = JSON.parse(t) as OllamaPullStreamLine;
          } catch {
            continue;
          }
          if (obj.error) {
            throw new Error(obj.error);
          }
          onLine(obj);
        }
        if (done) {
          break;
        }
      }
      const tail = buf.trim();
      if (tail) {
        try {
          const obj = JSON.parse(tail) as OllamaPullStreamLine;
          if (obj.error) {
            throw new Error(obj.error);
          }
          onLine(obj);
        } catch (e) {
          if (e instanceof SyntaxError) {
            return;
          }
          throw e;
        }
      }
    })();
  }

  deleteModel(model: string) {
    return this.http.post(`${apiBaseUrl}/api/ollama/models/delete`, { model }, { observe: 'response' });
  }

  importFromUrl(url: string, modelName: string) {
    return this.http.post(`${apiBaseUrl}/api/ollama/import-url`, { url, modelName }, { observe: 'response' });
  }
}
