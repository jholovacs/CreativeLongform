import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SceneWorkflowComponent } from './scene-workflow.component';

describe('SceneWorkflowComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.removeItem('clf.sceneWorkflow.qualityThresholds');
    await TestBed.configureTestingModule({
      imports: [SceneWorkflowComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should create and load books', () => {
    const fixture = TestBed.createComponent(SceneWorkflowComponent);
    fixture.detectChanges();
    const genReq = httpMock.expectOne((r) => r.url.includes('/api/settings/generation'));
    genReq.flush({ qualityAcceptMinScore: 75, qualityReviewOnlyMinScore: 55 });
    const booksReq = httpMock.expectOne((r) => r.url.includes('/odata/Books'));
    booksReq.flush({ value: [] });
    expect(fixture.componentInstance).toBeTruthy();
    expect(fixture.componentInstance.books).toEqual([]);
  });
});
