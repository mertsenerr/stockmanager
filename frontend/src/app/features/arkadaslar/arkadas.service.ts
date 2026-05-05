import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Friend {
  id: string;
  kullaniciId: string;
  adSoyad: string;
  email: string;
  rol: string;
  durum: string;
  giden: boolean;
}

export interface FriendListResponse {
  arkadaslar: Friend[];
  gelenIstekler: Friend[];
  gidenIstekler: Friend[];
}

export interface UserSearchResult {
  id: string;
  adSoyad: string;
  email: string;
  rol: string;
  arkadaslikDurumu: 'yok' | 'arkadas' | 'giden' | 'gelen';
}

@Injectable({ providedIn: 'root' })
export class ArkadasService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/arkadaslar`;

  list(): Observable<FriendListResponse> {
    return this.http.get<FriendListResponse>(this.base);
  }

  search(q: string): Observable<UserSearchResult[]> {
    const params = new HttpParams().set('q', q);
    return this.http.get<UserSearchResult[]>(`${this.base}/ara`, { params });
  }

  sendRequest(toUserId: string): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${this.base}/istek`, { toUserId });
  }

  accept(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/istek/${id}/kabul`, {});
  }

  reject(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/istek/${id}/red`, {});
  }

  remove(otherUserId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${otherUserId}`);
  }
}
