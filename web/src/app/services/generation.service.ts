import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { apiBaseUrl } from '../core/api-config';

export interface GenerationProgressPayload {
  runId: string;
  step: string | null;
  detail: string | null;
}

@Injectable({ providedIn: 'root' })
export class GenerationService {
  private readonly http = inject(HttpClient);

  startGeneration(sceneId: string, idempotencyKey?: string) {
    return this.http.post<{ id: string }>(`${apiBaseUrl}/api/scenes/${sceneId}/generation`, {
      idempotencyKey: idempotencyKey ?? null
    });
  }

  connectToRun(
    runId: string,
    handlers: {
      onStep?: (p: GenerationProgressPayload) => void;
      onAgentEdit?: (p: GenerationProgressPayload) => void;
      onRepair?: (p: GenerationProgressPayload) => void;
      onFinished?: (p: GenerationProgressPayload) => void;
    }
  ): HubConnection {
    const connection = new HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/generation`, { withCredentials: false })
      .configureLogging(LogLevel.Information)
      .build();

    connection.on('StepStarted', (payload: GenerationProgressPayload) => handlers.onStep?.(payload));
    connection.on('AgentEditTurn', (payload: GenerationProgressPayload) => handlers.onAgentEdit?.(payload));
    connection.on('RepairAttempt', (payload: GenerationProgressPayload) => handlers.onRepair?.(payload));
    connection.on('RunFinished', (payload: GenerationProgressPayload) => handlers.onFinished?.(payload));
    connection.on('RunStarted', (payload: GenerationProgressPayload) => handlers.onStep?.(payload));

    void connection
      .start()
      .then(() => connection.invoke('JoinRun', runId))
      .catch((err) => console.error('SignalR connection failed', err));

    return connection;
  }
}
