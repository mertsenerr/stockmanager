import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface SearchResultItem {
  id: string;
  label: string;
  subtitle?: string;
  badge?: string;
  route?: string;
}

export interface SearchResults {
  firmalar: SearchResultItem[];
  magazalar: SearchResultItem[];
  kullanicilar: SearchResultItem[];
  oturumlar: SearchResultItem[];
}

const EMPTY: SearchResults = { firmalar: [], magazalar: [], kullanicilar: [], oturumlar: [] };

@Injectable({ providedIn: 'root' })
export class SearchService {
  private readonly http = inject(HttpClient);

  search(query: string, limit = 6): Observable<SearchResults> {
    const q = (query ?? '').trim();
    if (q.length < 2) return of(EMPTY);
    const params = new HttpParams().set('q', q).set('limit', String(limit));
    return this.http
      .get<SearchResults>(`${environment.apiBaseUrl}/api/search`, { params })
      .pipe(catchError(() => of(EMPTY)));
  }
}
