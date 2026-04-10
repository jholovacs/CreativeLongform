import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppComponent } from './app.component';

/** Root shell: verifies the app bootstraps with router and default title. */
describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideRouter([])]
    }).compileComponents();
  });

  /**
   * System under test: {@link AppComponent}
   * Test case: Instantiate with router and read `title`.
   * Expected result: Component exists; `title` is the product string used in the template.
   * Why it's important: A broken root component blocks every route; title regression breaks branding/accessibility.
   */
  it('should create', () => {
    const fixture = TestBed.createComponent(AppComponent);
    expect(fixture.componentInstance).toBeTruthy();
    expect(fixture.componentInstance.title).toBe('Creative Longform');
  });
});
