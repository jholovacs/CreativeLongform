import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { SceneDraftComponent } from './scene-draft.component';

/** Draft workspace: loads nested book/scene data and resolves awaiting-review generation run. */
describe('SceneDraftComponent', () => {
  const sceneId = '550e8400-e29b-41d4-a716-446655440000';

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SceneDraftComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({ sceneId }) }
          }
        }
      ]
    }).compileComponents();
  });

  /**
   * System under test: {@link SceneDraftComponent} initial load.
   * Test case: Flush Books expand tree then GenerationRuns with enum status filter.
   * Expected result: `generationRunId` set from run row; `draftText` from scene; loading cleared.
   * Why it's important: Mismatch with OData filter syntax or wrong field breaks review mode and shows stale drafts.
   */
  it('loads books then GenerationRuns filter for awaiting review', () => {
    const fixture = TestBed.createComponent(SceneDraftComponent);
    const httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    const booksReq = httpMock.expectOne((r) => r.url.includes('/odata/Books'));
    booksReq.flush({
      value: [
        {
          id: 'book-1',
          title: 'Novel',
          chapters: [
            {
              id: 'ch-1',
              bookId: 'book-1',
              order: 1,
              title: 'Ch1',
              scenes: [
                {
                  id: sceneId,
                  chapterId: 'ch-1',
                  order: 1,
                  title: 'Scene',
                  synopsis: '',
                  instructions: '',
                  latestDraftText: 'Draft body.'
                }
              ]
            }
          ]
        }
      ]
    });

    const runsReq = httpMock.expectOne((r) => r.url.includes('/odata/GenerationRuns'));
    expect(runsReq.request.params.get('$filter')).toBe(
      `sceneId eq ${sceneId} and status eq CreativeLongform.Domain.Enums.GenerationRunStatus'AwaitingUserReview'`
    );
    runsReq.flush({ value: [{ id: 'run-xyz' }] });

    expect(fixture.componentInstance.generationRunId).toBe('run-xyz');
    expect(fixture.componentInstance.draftText).toBe('Draft body.');
    expect(fixture.componentInstance.loading).toBe(false);
  });
});
