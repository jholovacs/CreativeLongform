import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { apiBaseUrl } from '../core/api-config';
import { odataCount, odataRows } from '../core/odata-unwrap';
import {
  ApplyLinkCanonItem,
  ApplySuggestedLinksResponse,
  CreatedBookResponse,
  LinkCanonApplyResult,
  LinkCanonReviewResult,
  ODataCollection,
  ODataPagedResult,
  SceneWorldElementRow,
  TimelineEntry,
  WorldBuildingApplyResult,
  WorldBuildingSuggestedLink,
  WorldElement,
  WorldLinkRow,
  WorldLinksPage
} from '../models/entities';
import { map } from 'rxjs/operators';

@Injectable({ providedIn: 'root' })
export class WorldService {
  private readonly http = inject(HttpClient);

  /** Escape single quotes for OData string literals in filters. */
  private static escapeODataLiteral(s: string): string {
    return s.replace(/'/g, "''");
  }

  /** Lowercase for search terms (invariant English) so OData `tolower` matches stored text regardless of casing. */
  private static normalizeSearchLiteral(s: string): string {
    return WorldService.escapeODataLiteral(s.trim().toLocaleLowerCase('en-US'));
  }

  createBook(body: {
    title: string;
    storyToneAndStyle?: string;
    contentStyleNotes?: string;
    synopsis?: string;
  }) {
    return this.http.post<CreatedBookResponse>(`${apiBaseUrl}/api/books`, body);
  }

  bootstrapWorld(
    bookId: string,
    body: {
      storyToneAndStyle?: string;
      contentStyleNotes?: string;
      synopsis?: string;
      sourceText?: string;
    }
  ) {
    return this.http.post<WorldBuildingApplyResult>(`${apiBaseUrl}/api/books/${bookId}/world/bootstrap`, body);
  }

  suggestLinksForElement(bookId: string, elementId: string) {
    return this.http.post<WorldBuildingSuggestedLink[]>(`${apiBaseUrl}/api/books/${bookId}/world/suggest-links`, {
      elementId
    });
  }

  applySuggestedLinks(bookId: string, links: WorldBuildingSuggestedLink[]) {
    return this.http.post<ApplySuggestedLinksResponse>(
      `${apiBaseUrl}/api/books/${bookId}/world/apply-suggested-links`,
      {
        links: links.map((l) => ({
          fromWorldElementId: l.fromWorldElementId,
          toWorldElementId: l.toWorldElementId,
          relationLabel: l.relationLabel,
          relationDetail: l.relationDetail?.trim() ? l.relationDetail.trim() : null
        }))
      }
    );
  }

  /** LLM reviews links + timeline attachments for one world element (not persisted). */
  reviewLinkCanon(bookId: string, elementId: string) {
    return this.http.post<LinkCanonReviewResult>(
      `${apiBaseUrl}/api/books/${bookId}/world/elements/${elementId}/review-links-canon`,
      {}
    );
  }

  applyLinkCanonReview(bookId: string, items: ApplyLinkCanonItem[]) {
    return this.http.post<LinkCanonApplyResult>(
      `${apiBaseUrl}/api/books/${bookId}/world/apply-link-canon-review`,
      { items }
    );
  }

  /** All timeline rows for the book (visualization, exports), up to server max top. */
  getAllTimelineEntries(bookId: string) {
    return this.getTimelineEntriesPaged(bookId, { skip: 0, top: 1000, search: undefined });
  }

  getTimelineEntriesPaged(
    bookId: string,
    opts: { skip: number; top: number; search?: string }
  ) {
    let filter = `bookId eq ${bookId}`;
    const q = opts.search?.trim();
    if (q) {
      const e = WorldService.normalizeSearchLiteral(q);
      filter += ` and (contains(tolower(title),'${e}') or contains(tolower(summary),'${e}'))`;
    }
    return this.http
      .get<ODataCollection<TimelineEntry>>(`${apiBaseUrl}/odata/TimelineEntries`, {
        params: new HttpParams()
          .set('$filter', filter)
          .set('$orderby', 'sortKey')
          .set('$expand', 'Scene,WorldElement')
          .set('$skip', String(opts.skip))
          .set('$top', String(opts.top))
          .set('$count', 'true')
      })
      .pipe(
        map(
          (res): ODataPagedResult<TimelineEntry> => ({
            value: odataRows<TimelineEntry>(res),
            count: odataCount(res)
          })
        )
      );
  }

  createWorldTimelineEvent(
    bookId: string,
    body: {
      title: string;
      summary?: string | null;
      sortKey?: number;
      worldElementId?: string | null;
      currencyPairBase?: string | null;
      currencyPairQuote?: string | null;
      currencyPairAuthority?: string | null;
      currencyPairExchangeNote?: string | null;
    }
  ) {
    return this.http.post<{ id: string }>(`${apiBaseUrl}/api/books/${bookId}/timeline/world-events`, body);
  }

  patchTimelineEntry(
    entryId: string,
    body: {
      sortKey?: number;
      title?: string;
      summary?: string | null;
      worldElementId?: string | null;
      clearWorldElementId?: boolean;
      currencyPairBase?: string | null;
      currencyPairQuote?: string | null;
      currencyPairAuthority?: string | null;
      currencyPairExchangeNote?: string | null;
      clearCurrencyPair?: boolean;
    }
  ) {
    return this.http.patch(`${apiBaseUrl}/api/timeline-entries/${entryId}`, body);
  }

  deleteTimelineEntry(entryId: string) {
    return this.http.delete(`${apiBaseUrl}/api/timeline-entries/${entryId}`);
  }

  /**
   * All world elements for a book (dropdowns, pickers), ordered by title.
   * Capped by server max top (1000).
   */
  getWorldElements(bookId: string) {
    const filter = `bookId eq ${bookId}`;
    return this.http
      .get<ODataCollection<WorldElement>>(`${apiBaseUrl}/odata/WorldElements`, {
        params: new HttpParams()
          .set('$filter', filter)
          .set('$orderby', 'title')
          .set('$top', '1000')
      })
      .pipe(map((res) => ({ value: odataRows<WorldElement>(res) })));
  }

  getWorldElementsPaged(
    bookId: string,
    opts: { skip: number; top: number; search?: string }
  ) {
    let filter = `bookId eq ${bookId}`;
    const q = opts.search?.trim();
    if (q) {
      const e = WorldService.normalizeSearchLiteral(q);
      filter += ` and (contains(tolower(title),'${e}') or contains(tolower(summary),'${e}') or contains(tolower(cast(kind,'Edm.String')),'${e}') or contains(tolower(detail),'${e}'))`;
    }
    return this.http
      .get<ODataCollection<WorldElement>>(`${apiBaseUrl}/odata/WorldElements`, {
        params: new HttpParams()
          .set('$filter', filter)
          .set('$orderby', 'title')
          .set('$skip', String(opts.skip))
          .set('$top', String(opts.top))
          .set('$count', 'true')
      })
      .pipe(
        map(
          (res): ODataPagedResult<WorldElement> => ({
            value: odataRows<WorldElement>(res),
            count: odataCount(res)
          })
        )
      );
  }

  getWorldLinksPaged(
    bookId: string,
    opts: {
      skip: number;
      top: number;
      search?: string;
      worldElementId?: string;
      sortBy?: string;
      sortDesc?: boolean;
    }
  ) {
    let params = new HttpParams()
      .set('skip', String(opts.skip))
      .set('take', String(opts.top))
      .set('sortBy', opts.sortBy ?? 'relation')
      .set('sortDesc', String(opts.sortDesc ?? false));
    const q = opts.search?.trim();
    if (q) params = params.set('search', q);
    const wid = opts.worldElementId?.trim();
    if (wid) params = params.set('worldElementId', wid);
    return this.http.get<WorldLinksPage>(`${apiBaseUrl}/api/books/${bookId}/world/links`, { params });
  }

  createWorldElement(
    bookId: string,
    body: {
      kind: string;
      title: string;
      slug?: string | null;
      summary?: string;
      detail?: string;
      status?: string;
    }
  ) {
    return this.http.post<{ id: string }>(`${apiBaseUrl}/api/books/${bookId}/world/entries`, body);
  }

  patchWorldElement(
    elementId: string,
    body: {
      title?: string;
      slug?: string | null;
      summary?: string;
      detail?: string;
      kind?: string;
      status?: string;
    }
  ) {
    return this.http.patch(`${apiBaseUrl}/api/world-elements/${elementId}`, body);
  }

  deleteWorldElement(elementId: string) {
    return this.http.delete(`${apiBaseUrl}/api/world-elements/${elementId}`);
  }

  createWorldLink(
    bookId: string,
    body: {
      fromWorldElementId: string;
      toWorldElementId: string;
      relationLabel: string;
      relationDetail?: string | null;
    }
  ) {
    return this.http.post<{ id: string }>(`${apiBaseUrl}/api/books/${bookId}/world/links`, body);
  }

  patchWorldLink(
    linkId: string,
    body: { relationLabel: string; relationDetail: string | null }
  ) {
    return this.http.patch<void>(`${apiBaseUrl}/api/world-element-links/${linkId}`, body);
  }

  deleteWorldLink(linkId: string) {
    return this.http.delete(`${apiBaseUrl}/api/world-element-links/${linkId}`);
  }

  getSceneWorldElementIds(sceneId: string) {
    const filter = `sceneId eq ${sceneId}`;
    return this.http
      .get<ODataCollection<SceneWorldElementRow>>(`${apiBaseUrl}/odata/SceneWorldElements`, {
        params: new HttpParams()
          .set('$filter', filter)
          .set('$select', 'sceneId,worldElementId')
      })
      .pipe(map((res) => ({ value: odataRows<SceneWorldElementRow>(res) })));
  }

  patchStoryProfile(
    bookId: string,
    body: {
      storyToneAndStyle?: string;
      contentStyleNotes?: string | null;
      synopsis?: string | null;
      measurementPreset?: number;
      measurementSystemJson?: string | null;
    }
  ) {
    return this.http.patch(`${apiBaseUrl}/api/books/${bookId}/story-profile`, body);
  }

  extractFromText(bookId: string, text: string) {
    return this.http.post<WorldBuildingApplyResult>(
      `${apiBaseUrl}/api/books/${bookId}/world/extract`,
      { text }
    );
  }

  generateFromPrompt(bookId: string, prompt: string) {
    return this.http.post<WorldBuildingApplyResult>(
      `${apiBaseUrl}/api/books/${bookId}/world/generate`,
      { prompt }
    );
  }

  /** Full glossary as Markdown (A–Z, articles ignored for sort). Query useLlm enriches alternate names when true. */
  getGlossaryMarkdown(bookId: string, useLlm = true) {
    const params = new HttpParams().set('useLlm', String(useLlm));
    return this.http.get(`${apiBaseUrl}/api/books/${bookId}/world/glossary-markdown`, {
      responseType: 'text',
      params
    });
  }

  putSceneWorldElements(sceneId: string, worldElementIds: string[]) {
    return this.http.put(`${apiBaseUrl}/api/scenes/${sceneId}/world-elements`, {
      worldElementIds
    });
  }
}
