import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditPage, MagazaSapma, SaymanPerformans } from './rapor.models';

@Injectable({ providedIn: 'root' })
export class RaporService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  magazaSapma(from?: string, to?: string): Observable<MagazaSapma[]> {
    let p = new HttpParams();
    if (from) p = p.set('from', from);
    if (to) p = p.set('to', to);
    return this.http.get<MagazaSapma[]>(`${this.base}/api/raporlar/magaza-sapma`, { params: p });
  }

  saymanPerformans(from?: string, to?: string): Observable<SaymanPerformans[]> {
    let p = new HttpParams();
    if (from) p = p.set('from', from);
    if (to) p = p.set('to', to);
    return this.http.get<SaymanPerformans[]>(`${this.base}/api/raporlar/sayman-performans`, { params: p });
  }

  oturumExcelUrl(oturumId: string): string {
    return `${this.base}/api/raporlar/oturum/${oturumId}/excel`;
  }

  downloadOturumExcel(oturumId: string, accessToken: string, filenameHint = 'sayim'): Promise<void> {
    return fetch(this.oturumExcelUrl(oturumId), {
      headers: { Authorization: `Bearer ${accessToken}` },
      credentials: 'include',
    })
      .then(async (res) => {
        if (!res.ok) throw new Error('İndirme başarısız');
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') ?? '';
        const m = cd.match(/filename\*=UTF-8''([^;]+)/i) ?? cd.match(/filename=([^;]+)/i);
        const filename = m ? decodeURIComponent(m[1].replace(/"/g, '').trim()) : `${filenameHint}.xlsx`;
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(url);
      });
  }

  audit(opts: {
    from?: string; to?: string; kullaniciId?: string; aksiyon?: string;
    skip?: number; take?: number;
  }): Observable<AuditPage> {
    let p = new HttpParams();
    if (opts.from) p = p.set('from', opts.from);
    if (opts.to) p = p.set('to', opts.to);
    if (opts.kullaniciId) p = p.set('kullaniciId', opts.kullaniciId);
    if (opts.aksiyon) p = p.set('aksiyon', opts.aksiyon);
    if (opts.skip !== undefined) p = p.set('skip', String(opts.skip));
    if (opts.take !== undefined) p = p.set('take', String(opts.take));
    return this.http.get<AuditPage>(`${this.base}/api/audit`, { params: p });
  }
}
