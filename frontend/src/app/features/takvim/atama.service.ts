import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Atama, AtamaUpsert } from './takvim.models';

@Injectable({ providedIn: 'root' })
export class AtamaService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/atamalar`;

  list(fromIso: string, toIso: string): Observable<Atama[]> {
    return this.http.get<Atama[]>(this.base, {
      params: new HttpParams().set('from', fromIso).set('to', toIso),
    });
  }

  get(id: string): Observable<Atama> {
    return this.http.get<Atama>(`${this.base}/${id}`);
  }

  create(payload: AtamaUpsert): Observable<Atama> {
    return this.http.post<Atama>(this.base, payload);
  }

  update(id: string, payload: AtamaUpsert): Observable<Atama> {
    return this.http.put<Atama>(`${this.base}/${id}`, payload);
  }

  moveDate(id: string, tarihYmd: string): Observable<Atama> {
    return this.http.patch<Atama>(`${this.base}/${id}/tarih`, { tarih: tarihYmd });
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
