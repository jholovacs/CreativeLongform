import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { apiBaseUrl } from '../core/api-config';
import { odataRows } from '../core/odata-unwrap';
import { Book, ODataCollection } from '../models/entities';
import { map } from 'rxjs/operators';

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
}
