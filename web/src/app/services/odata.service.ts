import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { apiBaseUrl } from '../core/api-config';
import { odataRows } from '../core/odata-unwrap';
import { Book, LlmCall, ODataCollection } from '../models/entities';
import { map } from 'rxjs/operators';

/** OData enum literal (not <code>status eq 4</code>) — matches EDM <c>GenerationRunStatus</c>. */
const generationRunStatusAwaitingUserReview =
  "CreativeLongform.Domain.Enums.GenerationRunStatus'AwaitingUserReview'";

@Injectable({ providedIn: 'root' })
export class ODataService {
  private readonly http = inject(HttpClient);

  getBooksWithScenes() {
    const params = new HttpParams().set(
      '$expand',
      'Chapters($expand=Scenes)'
    );
    return this.http
      .get<ODataCollection<Book>>(`${apiBaseUrl}/odata/Books`, { params })
      .pipe(map((res) => ({ value: odataRows<Book>(res) })));
  }

  getBook(id: string) {
    const params = new HttpParams()
      .set('$filter', `id eq ${id}`)
      .set('$expand', 'Chapters($expand=Scenes)')
      .set('$top', '1');
    return this.http
      .get<ODataCollection<Book>>(`${apiBaseUrl}/odata/Books`, { params })
      .pipe(map((res) => ({ value: odataRows<Book>(res) })));
  }

  /**
   * Latest generation run for this scene waiting for finalize/correct (refresh-safe).
   * Returns null if none.
   */
  getGenerationRunAwaitingReview(sceneId: string) {
    /** Same Guid literal style as `getBook` / `getLlmCall`: unquoted UUID, not `guid'…'` (OData 400 on this stack). */
    const filter = `sceneId eq ${sceneId} and status eq ${generationRunStatusAwaitingUserReview}`;
    const params = new HttpParams()
      .set('$filter', filter)
      .set('$orderby', 'startedAt desc')
      .set('$top', '1')
      .set('$select', 'id');
    return this.http.get<ODataCollection<{ id: string }>>(`${apiBaseUrl}/odata/GenerationRuns`, { params }).pipe(
      map((res) => {
        const rows = odataRows<{ id: string }>(res);
        return rows[0]?.id ?? null;
      })
    );
  }

  /** Single LLM audit row (full request JSON + response text). */
  getLlmCall(id: string) {
    const params = new HttpParams().set('$filter', `id eq ${id}`).set('$top', '1');
    return this.http.get<ODataCollection<LlmCall>>(`${apiBaseUrl}/odata/LlmCalls`, { params }).pipe(
      map((res) => {
        const rows = odataRows<LlmCall>(res);
        if (rows.length === 0) {
          throw new Error('LlmCall not found');
        }
        return rows[0];
      })
    );
  }
}
