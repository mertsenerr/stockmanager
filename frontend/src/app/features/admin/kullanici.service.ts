import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { KullaniciCreate, KullaniciList, KullaniciUpdate } from './admin.models';

@Injectable({ providedIn: 'root' })
export class KullaniciService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/kullanicilar`;

  list(includeInactive = false): Observable<KullaniciList[]> {
    return this.http.get<KullaniciList[]>(this.base, {
      params: { includeInactive: String(includeInactive) },
    });
  }

  get(id: string): Observable<KullaniciList> {
    return this.http.get<KullaniciList>(`${this.base}/${id}`);
  }

  create(payload: KullaniciCreate): Observable<KullaniciList> {
    return this.http.post<KullaniciList>(this.base, payload);
  }

  update(id: string, payload: KullaniciUpdate): Observable<KullaniciList> {
    return this.http.put<KullaniciList>(`${this.base}/${id}`, payload);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
