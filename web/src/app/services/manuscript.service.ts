import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { apiBaseUrl } from '../core/api-config';

/** GET/PATCH manuscript endpoints: stored vs. assembled snapshot for book or chapter. */
export interface ManuscriptResponse {
  /** Author-edited `ManuscriptText` from DB, if any. */
  manuscriptText: string | null;
  /** Server-assembled text from child scenes (or chapter scenes for book). */
  computedAssembledText: string;
  /** Text the UI should show: manual override when set, else computed. */
  effectiveText: string;
}

@Injectable({ providedIn: 'root' })
export class ManuscriptService {
  private readonly http = inject(HttpClient);

  getBookManuscript(bookId: string) {
    return this.http.get<ManuscriptResponse>(`${apiBaseUrl}/api/books/${bookId}/manuscript`);
  }

  getChapterManuscript(chapterId: string) {
    return this.http.get<ManuscriptResponse>(`${apiBaseUrl}/api/chapters/${chapterId}/manuscript`);
  }

  patchBookManuscript(bookId: string, manuscriptText: string | null) {
    return this.http.patch<ManuscriptResponse>(`${apiBaseUrl}/api/books/${bookId}/manuscript`, {
      manuscriptText
    });
  }

  patchChapterManuscript(chapterId: string, manuscriptText: string | null) {
    return this.http.patch<ManuscriptResponse>(`${apiBaseUrl}/api/chapters/${chapterId}/manuscript`, {
      manuscriptText
    });
  }

  assembleBook(bookId: string) {
    return this.http.post<ManuscriptResponse>(`${apiBaseUrl}/api/books/${bookId}/manuscript/assemble`, {});
  }

  assembleChapter(chapterId: string) {
    return this.http.post<ManuscriptResponse>(`${apiBaseUrl}/api/chapters/${chapterId}/manuscript/assemble`, {});
  }
}
