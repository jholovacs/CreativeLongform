import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs/operators';
import { apiBaseUrl } from '../core/api-config';
import { odataCount, odataRows } from '../core/odata-unwrap';
import { Book, Chapter, ODataCollection, Scene } from '../models/entities';

/** Scene row from OData with expanded chapter and book. */
export interface SceneAdminRow extends Scene {
  chapter?: Chapter & { book?: Pick<Book, 'id' | 'title'> };
}

export interface SceneAdminPage {
  items: SceneAdminRow[];
  totalCount: number;
}

export type SceneAdminSortKey = 'story' | 'titleAsc' | 'titleDesc';

@Injectable({ providedIn: 'root' })
export class SceneAdminService {
  private readonly http = inject(HttpClient);

  private static escapeODataLiteral(s: string): string {
    return s.replace(/'/g, "''");
  }

  private static searchLiteral(q: string): string {
    return SceneAdminService.escapeODataLiteral(q.trim().toLocaleLowerCase('en-US'));
  }

  loadBooks() {
    return this.http
      .get<ODataCollection<Book>>(`${apiBaseUrl}/odata/Books`, {
        params: new HttpParams()
          .set('$select', 'id,title')
          .set('$expand', 'Chapters($select=id,bookId,order,title)')
          .set('$orderby', 'title')
      })
      .pipe(map((res) => odataRows<Book>(res)));
  }

  listScenes(opts: {
    bookIdFilter: string | null;
    search: string;
    sortKey: SceneAdminSortKey;
    skip: number;
    top: number;
  }) {
    const orderBy =
      opts.sortKey === 'titleAsc'
        ? 'title asc'
        : opts.sortKey === 'titleDesc'
          ? 'title desc'
          : 'Chapter/Book/Title asc,Chapter/Order asc,Order asc';

    const filterParts: string[] = [];
    if (opts.bookIdFilter) {
      filterParts.push(`Chapter/BookId eq ${opts.bookIdFilter}`);
    }
    const q = opts.search.trim();
    if (q) {
      const e = SceneAdminService.searchLiteral(q);
      filterParts.push(
        `(contains(tolower(title),'${e}') or contains(tolower(coalesce(synopsis,'')),'${e}') or contains(tolower(instructions),'${e}'))`
      );
    }
    const filter = filterParts.length ? filterParts.join(' and ') : undefined;

    let params = new HttpParams()
      .set('$expand', 'Chapter($expand=Book($select=id,title))')
      .set('$orderby', orderBy)
      .set('$skip', String(opts.skip))
      .set('$top', String(opts.top))
      .set('$count', 'true');
    if (filter) {
      params = params.set('$filter', filter);
    }

    return this.http.get<ODataCollection<SceneAdminRow>>(`${apiBaseUrl}/odata/Scenes`, { params }).pipe(
      map((res) => ({
        items: odataRows<SceneAdminRow>(res),
        totalCount: odataCount(res) ?? odataRows(res).length
      }))
    );
  }

  createScene(chapterId: string, body: { title?: string | null }) {
    return this.http.post<{ id: string; chapterId: string; order: number; title: string }>(
      `${apiBaseUrl}/api/chapters/${chapterId}/scenes`,
      { title: body.title ?? undefined }
    );
  }

  patchScene(
    sceneId: string,
    body: {
      title: string;
      synopsis: string;
      instructions: string;
      expectedEndStateNotes: string;
      narrativePerspective: string;
      narrativeTense: string;
      beginningStateJson: string;
    }
  ) {
    return this.http.patch(`${apiBaseUrl}/api/scenes/${sceneId}`, body);
  }

  deleteScene(sceneId: string) {
    return this.http.delete(`${apiBaseUrl}/api/scenes/${sceneId}`);
  }
}
