import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Magaza, MagazaUpsert } from './admin.models';

@Injectable({ providedIn: 'root' })
export class MagazaService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/magazalar`;

  list(opts: { firmaId?: string; includeInactive?: boolean } = {}): Observable<Magaza[]> {
    let params = new HttpParams();
    if (opts.firmaId) params = params.set('firmaId', opts.firmaId);
    if (opts.includeInactive) params = params.set('includeInactive', 'true');
    return this.http.get<Magaza[]>(this.base, { params });
  }

  get(id: string): Observable<Magaza> {
    return this.http.get<Magaza>(`${this.base}/${id}`);
  }

  create(payload: MagazaUpsert): Observable<Magaza> {
    return this.http.post<Magaza>(this.base, payload);
  }

  update(id: string, payload: MagazaUpsert): Observable<Magaza> {
    return this.http.put<Magaza>(`${this.base}/${id}`, payload);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
