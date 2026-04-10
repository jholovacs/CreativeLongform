import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SceneWorkflowService } from './scene-workflow.service';

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

  it('getDraftRecommendations posts draft text', () => {
    const sceneId = '550e8400-e29b-41d4-a716-446655440000';
    const draft = 'Paragraph one.\n\nParagraph two.';
    service.getDraftRecommendations(sceneId, draft).subscribe();
    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url.endsWith(`/api/scenes/${sceneId}/draft/recommendations`));
    expect(req.request.body).toEqual({ draftText: draft });
    req.flush({ items: [] });
  });

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

  it('patchScene sends partial body', () => {
    const sceneId = '770e8400-e29b-41d4-a716-446655440002';
    service.patchScene(sceneId, { synopsis: 'Beat' }).subscribe();
    const req = httpMock.expectOne((r) => r.method === 'PATCH' && r.url.endsWith(`/api/scenes/${sceneId}`));
    expect(req.request.body).toEqual({ synopsis: 'Beat' });
    req.flush({});
  });
});
