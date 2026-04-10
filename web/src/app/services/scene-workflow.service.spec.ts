import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SceneWorkflowService } from './scene-workflow.service';

/** HTTP wrapper for scene workflow REST endpoints (recommendations, context, patch). */
describe('SceneWorkflowService', () => {
  let service: SceneWorkflowService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), SceneWorkflowService]
    });
    service = TestBed.inject(SceneWorkflowService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  /**
   * System under test: {@link SceneWorkflowService.getDraftRecommendations}
   * Test case: POST with scene id and full draft text.
   * Expected result: URL and JSON body `{ draftText }` match API.
   * Why it's important: Wrong shape fails the recommendations endpoint and blocks inline editing assists.
   */
  it('getDraftRecommendations posts draft text', () => {
    const sceneId = '550e8400-e29b-41d4-a716-446655440000';
    const draft = 'Paragraph one.\n\nParagraph two.';
    service.getDraftRecommendations(sceneId, draft).subscribe();
    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith(`/api/scenes/${sceneId}/draft/recommendations`));
    expect(req.request.body).toEqual({ draftText: draft });
    req.flush({ items: [] });
  });

  /**
   * System under test: {@link SceneWorkflowService.getWorkflowContext}
   * Test case: GET workflow-context; flush minimal DTO.
   * Expected result: Parsed object exposes `hasPreviousScene` and nullable previous state.
   * Why it's important: Continuity defaults (POV, tense, state) depend on this payload for the workflow form.
   */
  it('getWorkflowContext GETs workflow-context', (done) => {
    const sceneId = '660e8400-e29b-41d4-a716-446655440001';
    service.getWorkflowContext(sceneId).subscribe((ctx) => {
      expect(ctx.hasPreviousScene).toBe(false);
      expect(ctx.previousSceneEndStateJson).toBeNull();
      done();
    });
    const req = httpMock.expectOne((r) => r.url.endsWith(`/api/scenes/${sceneId}/workflow-context`));
    req.flush({
      hasPreviousScene: false,
      previousSceneEndStateJson: null,
      defaultNarrativePerspective: null,
      defaultNarrativeTense: null
    });
  });

  /**
   * System under test: {@link SceneWorkflowService.patchScene}
   * Test case: PATCH with partial `{ synopsis }`.
   * Expected result: Method and body match scene update API.
   * Why it's important: Partial updates must not send unrelated fields that could overwrite server state.
   */
  it('patchScene sends partial body', () => {
    const sceneId = '770e8400-e29b-41d4-a716-446655440002';
    service.patchScene(sceneId, { synopsis: 'Beat' }).subscribe();
    const req = httpMock.expectOne((r) => r.method === 'PATCH' && r.url.endsWith(`/api/scenes/${sceneId}`));
    expect(req.request.body).toEqual({ synopsis: 'Beat' });
    req.flush({});
  });
});
