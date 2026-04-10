import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ODataService } from './odata.service';

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

  it('getBooksWithScenes requests OData with Chapters and Scenes expanded', () => {
    service.getBooksWithScenes().subscribe();
    const req = httpMock.expectOne((r) => r.url.endsWith('/odata/Books'));
    expect(req.request.params.get('$expand')).toBe('Chapters($expand=Scenes)');
    req.flush({ value: [] });
  });

  it('getBook filters by id', () => {
    const id = '550e8400-e29b-41d4-a716-446655440000';
    service.getBook(id).subscribe();
    const req = httpMock.expectOne((r) => r.url.endsWith('/odata/Books'));
    expect(req.request.params.get('$filter')).toContain(id);
    expect(req.request.params.get('$top')).toBe('1');
    req.flush({ value: [] });
  });

  it('getGenerationRunAwaitingReview uses unquoted scene id and status 4', () => {
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

  it('getGenerationRunAwaitingReview returns null when OData has no rows', () => {
    let result: string | null | undefined;
    service.getGenerationRunAwaitingReview('550e8400-e29b-41d4-a716-446655440000').subscribe((id) => (result = id));
    const req = httpMock.expectOne((r) => r.url.includes('/odata/GenerationRuns'));
    req.flush({ value: [] });
    expect(result).toBeNull();
  });

  it('getLlmCall filters by id', () => {
    const id = 'aa0e8400-e29b-41d4-a716-446655440099';
    service.getLlmCall(id).subscribe();
    const req = httpMock.expectOne((r) => r.url.endsWith('/odata/LlmCalls'));
    expect(req.request.params.get('$filter')).toContain(id);
    expect(req.request.params.get('$top')).toBe('1');
    req.flush({ value: [{ id, step: 'Draft', model: 'm', requestJson: '{}', responseText: 'ok' }] });
  });
});
