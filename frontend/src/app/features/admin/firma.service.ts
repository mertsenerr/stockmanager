import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Firma, FirmaUpsert } from './admin.models';

@Injectable({ providedIn: 'root' })
export class FirmaService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/firmalar`;

  list(includeInactive = false): Observable<Firma[]> {
    return this.http.get<Firma[]>(this.base, {
      params: { includeInactive: String(includeInactive) },
    });
  }

  get(id: string): Observable<Firma> {
    return this.http.get<Firma>(`${this.base}/${id}`);
  }

  create(payload: FirmaUpsert): Observable<Firma> {
    return this.http.post<Firma>(this.base, payload);
  }

  update(id: string, payload: FirmaUpsert): Observable<Firma> {
    return this.http.put<Firma>(`${this.base}/${id}`, payload);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
