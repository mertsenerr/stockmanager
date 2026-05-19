import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { OzelRapor, OzelRaporUpsert } from './ozel-rapor.models';
import { AuthService } from '../../core/auth/auth.service';

@Injectable({ providedIn: 'root' })
export class OzelRaporService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly base = `${environment.apiBaseUrl}/api/ozel-raporlar`;

  list(): Observable<OzelRapor[]> {
    return this.http.get<OzelRapor[]>(this.base);
  }

  create(payload: OzelRaporUpsert): Observable<OzelRapor> {
    return this.http.post<OzelRapor>(this.base, payload);
  }

  update(id: string, payload: OzelRaporUpsert): Observable<OzelRapor> {
    return this.http.put<OzelRapor>(`${this.base}/${id}`, payload);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  uploadFiles(id: string, files: File[], belgeTipiId?: string | null): Observable<OzelRapor> {
    const fd = new FormData();
    for (const f of files) fd.append('files', f, f.name);
    if (belgeTipiId) fd.append('belgeTipiId', belgeTipiId);
    return this.http.post<OzelRapor>(`${this.base}/${id}/files`, fd);
  }

  removeFile(raporId: string, fileId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${raporId}/files/${fileId}`);
  }

  async download(raporId: string, fileId: string, filename: string): Promise<void> {
    return this.fetchDownload(`${this.base}/${raporId}/files/${fileId}/download`, filename);
  }

  /** İmza + kaşe bindirilmiş PDF'i indirir. Sadece PDF dosyalar için çalışır. */
  async downloadSigned(raporId: string, fileId: string, filename: string): Promise<void> {
    return this.fetchDownload(`${this.base}/${raporId}/files/${fileId}/signed`, filename);
  }

  private async fetchDownload(url: string, filename: string): Promise<void> {
    const token = this.auth.accessToken();
    if (!token) throw new Error('Oturum yok');
    const res = await fetch(url, {
      headers: { Authorization: `Bearer ${token}` },
      credentials: 'include',
    });
    if (!res.ok) {
      // Backend 422 verirse JSON body içinde "message" döner
      let msg = 'İndirme başarısız';
      try {
        const body = await res.json();
        if (body?.message) msg = body.message;
      } catch { /* JSON değil, ignore */ }
      throw new Error(msg);
    }
    const blob = await res.blob();
    const link = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = link;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(link);
  }

  imzaAt(raporId: string, fileId: string, rol: string, dataUri: string): Observable<unknown> {
    return this.http.post(`${this.base}/${raporId}/files/${fileId}/imza`, {
      rol,
      imzaGorseliDataUri: dataUri,
    });
  }

  imzaSil(raporId: string, fileId: string, imzaId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${raporId}/files/${fileId}/imza/${imzaId}`);
  }

  kaseBas(raporId: string, fileId: string): Observable<unknown> {
    return this.http.post(`${this.base}/${raporId}/files/${fileId}/kase`, {});
  }

  kaseSil(raporId: string, fileId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${raporId}/files/${fileId}/kase`);
  }
}
