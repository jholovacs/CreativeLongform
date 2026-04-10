import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { GenerationService } from './generation.service';

describe('GenerationService', () => {
  let service: GenerationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), GenerationService]
    });
    service = TestBed.inject(GenerationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('startGeneration posts to scene generation endpoint', () => {
    const sceneId = '550e8400-e29b-41d4-a716-446655440000';
    service.startGeneration(sceneId, { idempotencyKey: 'key-1' }).subscribe();
    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url.includes(`/api/scenes/${sceneId}/generation`));
    expect(req.request.body).toEqual({
      idempotencyKey: 'key-1',
      stopAfterDraft: false,
      minWordsOverride: null,
      maxWordsOverride: null,
      skipQualityGate: false,
      qualityAcceptMinScore: null,
      qualityReviewOnlyMinScore: null
    });
    req.flush({ id: 'run-id' });
  });

  it('connectToRun returns a hub connection', () => {
    const conn = service.connectToRun('run-1', {});
    expect(conn).toBeTruthy();
  });

  it('cancelGeneration posts to cancel endpoint', () => {
    const sceneId = '550e8400-e29b-41d4-a716-446655440000';
    const runId = '660e8400-e29b-41d4-a716-446655440001';
    service.cancelGeneration(sceneId, runId).subscribe();
    const req = httpMock.expectOne(
      (r) => r.method === 'POST' && r.url.endsWith(`/api/scenes/${sceneId}/generation/${runId}/cancel`)
    );
    expect(req.request.body).toEqual({});
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('finalizeGeneration posts generation run and draft', () => {
    const sceneId = '550e8400-e29b-41d4-a716-446655440000';
    const runId = '660e8400-e29b-41d4-a716-446655440001';
    service
      .finalizeGeneration(sceneId, {
        generationRunId: runId,
        acceptedDraftText: 'Final prose.',
        approvedStateTableJson: null
      })
      .subscribe();
    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith(`/api/scenes/${sceneId}/generation/finalize`));
    expect(req.request.body).toEqual({
      generationRunId: runId,
      acceptedDraftText: 'Final prose.',
      approvedStateTableJson: null
    });
    req.flush({ stateTableJson: '{}', nextSceneId: null });
  });

  it('correctDraft posts instruction and optional selection', () => {
    const sceneId = '550e8400-e29b-41d4-a716-446655440000';
    service
      .correctDraft(sceneId, {
        generationRunId: 'run-1',
        instruction: 'Tighten dialogue',
        currentDraftText: 'Hello',
        selectionStart: 0,
        selectionEnd: 5
      })
      .subscribe();
    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith(`/api/scenes/${sceneId}/generation/correct`));
    expect(req.request.body).toEqual({
      generationRunId: 'run-1',
      instruction: 'Tighten dialogue',
      currentDraftText: 'Hello',
      selectionStart: 0,
      selectionEnd: 5
    });
    req.flush({ correctedDraftText: 'Hi', pendingPostStateJson: null });
  });

  it('startGeneration sends stopAfterDraft and quality overrides when provided', () => {
    const sceneId = '550e8400-e29b-41d4-a716-446655440000';
    service
      .startGeneration(sceneId, {
        stopAfterDraft: true,
        minWordsOverride: 1000,
        maxWordsOverride: 2000,
        qualityAcceptMinScore: 80,
        qualityReviewOnlyMinScore: 60
      })
      .subscribe();
    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url.includes(`/api/scenes/${sceneId}/generation`));
    expect(req.request.body).toEqual({
      idempotencyKey: null,
      stopAfterDraft: true,
      minWordsOverride: 1000,
      maxWordsOverride: 2000,
      skipQualityGate: false,
      qualityAcceptMinScore: 80,
      qualityReviewOnlyMinScore: 60
    });
    req.flush({ id: 'run-id' });
  });
});
