import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { BookWorldComponent } from './book-world.component';

describe('BookWorldComponent', () => {
  let httpMock: HttpTestingController;
  const bookId = '550e8400-e29b-41d4-a716-446655440000';

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BookWorldComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => (key === 'bookId' ? bookId : null)
              }
            },
            paramMap: of(convertToParamMap({ bookId }))
          }
        }
      ]
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should create and load book, world elements, and links', () => {
    const fixture = TestBed.createComponent(BookWorldComponent);
    fixture.detectChanges();

    const bookReq = httpMock.expectOne((r) => r.url.includes('/odata/Books'));
    expect(bookReq.request.params.get('$filter')).toContain(bookId);
    bookReq.flush({ value: [] });

    const pickerReq = httpMock.expectOne(
      (r) => r.url.includes('/odata/WorldElements') && r.params.get('$top') === '1000'
    );
    pickerReq.flush({ value: [] });

    const worldPageReq = httpMock.expectOne(
      (r) =>
        r.url.includes('/odata/WorldElements') &&
        r.params.get('$skip') === '0' &&
        r.params.get('$count') === 'true'
    );
    worldPageReq.flush({ value: [], '@odata.count': 0 });

    const timelineReq = httpMock.expectOne(
      (r) => r.url.includes('/odata/TimelineEntries') && r.params.get('$count') === 'true'
    );
    timelineReq.flush({ value: [], '@odata.count': 0 });

    const linksReq = httpMock.expectOne(
      (r) =>
        r.url.endsWith(`/api/books/${bookId}/world/links`) &&
        r.params.get('skip') === '0' &&
        r.params.get('take') === '10'
    );
    linksReq.flush({ totalCount: 0, items: [] });

    expect(fixture.componentInstance.bookId).toBe(bookId);
  });
});
