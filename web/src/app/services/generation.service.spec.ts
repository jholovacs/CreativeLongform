import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { GenerationService } from './generation.service';

/** Generation REST + SignalR entry points used by scene draft and workflow UIs. */
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

  /**
   * System under test: {@link GenerationService.startGeneration} default options.
   * Test case: POST with only `idempotencyKey`.
   * Expected result: Body includes null overrides and boolean defaults matching API contract.
   * Why it's important: Drift in default flags would change pipeline behavior (e.g. quality gate) between clients.
   */
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

  /**
   * System under test: {@link GenerationService.connectToRun}
   * Test case: Request hub connection for a run id.
   * Expected result: Non-null connection object (SignalR).
   * Why it's important: Progress UI depends on hub wiring; a null here means no live status.
   */
  it('connectToRun returns a hub connection', () => {
    const conn = service.connectToRun('run-1', {});
    expect(conn).toBeTruthy();
  });

  /**
   * System under test: {@link GenerationService.cancelGeneration}
   * Test case: POST cancel with scene and run ids.
   * Expected result: Correct path; empty JSON body.
   * Why it's important: Wrong route leaves orphan runs consuming quota and confusing status.
   */
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

  /**
   * System under test: {@link GenerationService.finalizeGeneration}
   * Test case: POST accepted draft and optional state table.
   * Expected result: Body matches finalize DTO.
   * Why it's important: Finalize promotes draft to manuscript; wrong payload corrupts continuity state.
   */
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

  /**
   * System under test: {@link GenerationService.correctDraft}
   * Test case: POST instruction with selection range over current draft.
   * Expected result: Body includes instruction, full draft, and selection indices.
   * Why it's important: Targeted corrections must send selection so the backend replaces the right span.
   */
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

  /**
   * System under test: {@link GenerationService.startGeneration} with workflow overrides.
   * Test case: `stopAfterDraft`, word bounds, quality score overrides.
   * Expected result: All overrides forwarded; idempotency null when omitted.
   * Why it's important: Scene workflow relies on these knobs for draft-only runs and quality thresholds.
   */
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
