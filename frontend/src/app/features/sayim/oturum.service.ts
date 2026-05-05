import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ExcelImportPayload,
  OturumCreate,
  OturumDetail,
  OturumDurum,
  OturumList,
  OturumUrun,
} from './sayim.models';

export interface InvitableUser {
  id: string;
  adSoyad: string;
  email: string;
  rol: string;
  zatenKatilimci: boolean;
}

@Injectable({ providedIn: 'root' })
export class OturumService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/oturumlar`;

  list(opts: { magazaId?: string; durum?: string; from?: string; to?: string } = {}): Observable<OturumList[]> {
    let params = new HttpParams();
    if (opts.magazaId) params = params.set('magazaId', opts.magazaId);
    if (opts.durum) params = params.set('durum', opts.durum);
    if (opts.from) params = params.set('from', opts.from);
    if (opts.to) params = params.set('to', opts.to);
    return this.http.get<OturumList[]>(this.base, { params });
  }

  get(id: string): Observable<OturumDetail> {
    return this.http.get<OturumDetail>(`${this.base}/${id}`);
  }

  create(payload: OturumCreate): Observable<OturumDetail> {
    return this.http.post<OturumDetail>(this.base, payload);
  }

  changeDurum(id: string, durum: OturumDurum): Observable<OturumDetail> {
    return this.http.patch<OturumDetail>(`${this.base}/${id}/durum`, { durum });
  }

  cancel(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  /** Hard delete — kayıt veritabanından kaldırılır (soft cancel'dan farklı). */
  hardDelete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}/permanent`);
  }

  listInvitableUsers(id: string): Observable<InvitableUser[]> {
    return this.http.get<InvitableUser[]>(`${this.base}/${id}/davet-edilebilir`);
  }

  importExcel(id: string, payload: ExcelImportPayload): Observable<OturumDetail> {
    return this.http.post<OturumDetail>(`${this.base}/${id}/excel`, payload);
  }

  patchUrun(
    oturumId: string,
    urunId: string,
    body: Partial<{
      sayilanStok: number;
      durum: string;
      atananSaymanId: string;
      yorumEkle: string;
      barkod: string;
      urunAdi: string;
      sistemStok: number;
    }>,
  ): Observable<OturumUrun> {
    return this.http.patch<OturumUrun>(`${this.base}/${oturumId}/urun/${urunId}`, body);
  }

  deleteUrun(oturumId: string, urunId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${oturumId}/urun/${urunId}`);
  }
}
