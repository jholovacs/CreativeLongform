import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WorldService } from './world.service';

/** World elements OData and scene–element link REST helpers. */
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

  /**
   * System under test: {@link WorldService.getWorldElements}
   * Test case: Request elements for a book id.
   * Expected result: OData filter contains book id; large `$top` for picker lists.
   * Why it's important: Wrong filter leaks other books’ elements into the scene linker.
   */
  it('getWorldElements filters by book id', () => {
    const bookId = '550e8400-e29b-41d4-a716-446655440000';
    service.getWorldElements(bookId).subscribe();
    const req = httpMock.expectOne((r) => r.url.endsWith('/odata/WorldElements'));
    expect(req.request.params.get('$filter')).toContain(bookId);
    expect(req.request.params.get('$top')).toBe('1000');
    req.flush({ value: [] });
  });

  /**
   * System under test: {@link WorldService.putSceneWorldElements}
   * Test case: PUT replacement set of world element ids for a scene.
   * Expected result: JSON body `{ worldElementIds }` matches API.
   * Why it's important: Scene-scoped world links drive LLM context; wrong body shape fails silently or 400s.
   */
  it('putSceneWorldElements sends world element ids', () => {
    const sceneId = '660e8400-e29b-41d4-a716-446655440001';
    service.putSceneWorldElements(sceneId, ['a', 'b']).subscribe();
    const req = httpMock.expectOne((r) => r.method === 'PUT' && r.url.includes(`/api/scenes/${sceneId}/world-elements`));
    expect(req.request.body).toEqual({ worldElementIds: ['a', 'b'] });
    req.flush({});
  });
});
