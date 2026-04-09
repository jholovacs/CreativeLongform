import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WorldService } from './world.service';

describe('WorldService', () => {
  let service: WorldService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), WorldService]
    });
    service = TestBed.inject(WorldService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getWorldElements filters by book id', () => {
    const bookId = '550e8400-e29b-41d4-a716-446655440000';
    service.getWorldElements(bookId).subscribe();
    const req = httpMock.expectOne((r) => r.url.endsWith('/odata/WorldElements'));
    expect(req.request.params.get('$filter')).toContain(bookId);
    expect(req.request.params.get('$top')).toBe('1000');
    req.flush({ value: [] });
  });

  it('putSceneWorldElements sends world element ids', () => {
    const sceneId = '660e8400-e29b-41d4-a716-446655440001';
    service.putSceneWorldElements(sceneId, ['a', 'b']).subscribe();
    const req = httpMock.expectOne((r) => r.method === 'PUT' && r.url.includes(`/api/scenes/${sceneId}/world-elements`));
    expect(req.request.body).toEqual({ worldElementIds: ['a', 'b'] });
    req.flush({});
  });
});
