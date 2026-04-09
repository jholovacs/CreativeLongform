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
    service.startGeneration(sceneId, 'key-1').subscribe();
    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url.includes(`/api/scenes/${sceneId}/generation`));
    expect(req.request.body).toEqual({ idempotencyKey: 'key-1' });
    req.flush({ id: 'run-id' });
  });

  it('connectToRun returns a hub connection', () => {
    const conn = service.connectToRun('run-1', {});
    expect(conn).toBeTruthy();
  });
});
