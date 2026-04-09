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
});
