import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BelgeTipi, BelgeTipiUpsert } from './belge-tipi.models';

@Injectable({ providedIn: 'root' })
export class BelgeTipiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/belge-tipleri`;

  list(opts?: { firmaId?: string; includeArchived?: boolean }): Observable<BelgeTipi[]> {
    let params = new HttpParams();
    if (opts?.firmaId) params = params.set('firmaId', opts.firmaId);
    if (opts?.includeArchived) params = params.set('includeArchived', 'true');
    return this.http.get<BelgeTipi[]>(this.base, { params });
  }

  get(id: string): Observable<BelgeTipi> {
    return this.http.get<BelgeTipi>(`${this.base}/${id}`);
  }

  create(payload: BelgeTipiUpsert): Observable<BelgeTipi> {
    return this.http.post<BelgeTipi>(this.base, payload);
  }

  update(id: string, payload: BelgeTipiUpsert): Observable<BelgeTipi> {
    return this.http.put<BelgeTipi>(`${this.base}/${id}`, payload);
  }
}
