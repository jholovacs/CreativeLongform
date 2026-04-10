import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ODataService } from './odata.service';

/** OData query builders for books, generation runs, and audit rows. */
describe('ODataService', () => {
  let service: ODataService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), ODataService]
    });
    service = TestBed.inject(ODataService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  /**
   * System under test: {@link ODataService.getBooksWithScenes}
   * Test case: Trigger default books query.
   * Expected result: `$expand` chains Chapters and Scenes for nested navigation UI.
   * Why it's important: Missing expand forces N+1 requests or empty chapter lists in the tree.
   */
  it('getBooksWithScenes requests OData with Chapters and Scenes expanded', () => {
    service.getBooksWithScenes().subscribe();
    const req = httpMock.expectOne((r) => r.url.endsWith('/odata/Books'));
    expect(req.request.params.get('$expand')).toBe('Chapters($expand=Scenes)');
    req.flush({ value: [] });
  });

  /**
   * System under test: {@link ODataService.getBook}
   * Test case: Load single book by guid string.
   * Expected result: `$filter` contains id; `$top=1`.
   * Why it's important: Wrong filter returns the wrong book or multiple rows.
   */
  it('getBook filters by id', () => {
    const id = '550e8400-e29b-41d4-a716-446655440000';
    service.getBook(id).subscribe();
    const req = httpMock.expectOne((r) => r.url.endsWith('/odata/Books'));
    expect(req.request.params.get('$filter')).toContain(id);
    expect(req.request.params.get('$top')).toBe('1');
    req.flush({ value: [] });
  });

  /**
   * System under test: {@link ODataService.getGenerationRunAwaitingReview}
   * Test case: Resolve run for a scene in `AwaitingUserReview` using enum literal (not numeric).
   * Expected result: Filter uses unquoted Guid and OData enum member syntax; subscribe returns run id from first row.
   * Why it's important: OData rejects `status eq 4` for enum properties; this must match the API EDM and backend tests.
   */
  it('getGenerationRunAwaitingReview uses unquoted scene id and enum status literal', () => {
    const sceneId = 'e89d7b9a-fd98-4a88-8352-445c27524489';
    let result: string | null | undefined;
    service.getGenerationRunAwaitingReview(sceneId).subscribe((id) => (result = id));
    const req = httpMock.expectOne((r) => r.url.includes('/odata/GenerationRuns'));
    expect(req.request.params.get('$filter')).toBe(
      `sceneId eq ${sceneId} and status eq CreativeLongform.Domain.Enums.GenerationRunStatus'AwaitingUserReview'`
    );
    expect(req.request.params.get('$orderby')).toBe('startedAt desc');
    expect(req.request.params.get('$top')).toBe('1');
    expect(req.request.params.get('$select')).toBe('id');
    req.flush({ value: [{ id: 'run-a' }] });
    expect(result).toBe('run-a');
  });

  /**
   * System under test: {@link ODataService.getGenerationRunAwaitingReview} empty result.
   * Test case: OData returns empty `value` array.
   * Expected result: Observable emits `null`.
   * Why it's important: Callers distinguish “no run” from a valid id for gating review UI.
   */
  it('getGenerationRunAwaitingReview returns null when OData has no rows', () => {
    let result: string | null | undefined;
    service.getGenerationRunAwaitingReview('550e8400-e29b-41d4-a716-446655440000').subscribe((id) => (result = id));
    const req = httpMock.expectOne((r) => r.url.includes('/odata/GenerationRuns'));
    req.flush({ value: [] });
    expect(result).toBeNull();
  });

  /**
   * System under test: {@link ODataService.getLlmCall}
   * Test case: Fetch audit row by id.
   * Expected result: `$filter` contains id; `$top=1`.
   * Why it's important: Debug/inspect views need exact row lookup for a given LLM call id.
   */
  it('getLlmCall filters by id', () => {
    const id = 'aa0e8400-e29b-41d4-a716-446655440099';
    service.getLlmCall(id).subscribe();
    const req = httpMock.expectOne((r) => r.url.endsWith('/odata/LlmCalls'));
    expect(req.request.params.get('$filter')).toContain(id);
    expect(req.request.params.get('$top')).toBe('1');
    req.flush({ value: [{ id, step: 'Draft', model: 'm', requestJson: '{}', responseText: 'ok' }] });
  });
});
