import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
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
            }
          }
        }
      ]
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should create and load book and world elements', () => {
    const fixture = TestBed.createComponent(BookWorldComponent);
    fixture.detectChanges();

    const bookReq = httpMock.expectOne((r) => r.url.includes('/odata/Books'));
    expect(bookReq.request.params.get('$filter')).toContain(bookId);
    bookReq.flush({ value: [] });

    const worldReq = httpMock.expectOne((r) => r.url.includes('/odata/WorldElements'));
    worldReq.flush({ value: [] });

    expect(fixture.componentInstance.bookId).toBe(bookId);
  });
});
