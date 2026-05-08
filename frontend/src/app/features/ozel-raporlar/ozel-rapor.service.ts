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

  uploadFiles(id: string, files: File[]): Observable<OzelRapor> {
    const fd = new FormData();
    for (const f of files) fd.append('files', f, f.name);
    return this.http.post<OzelRapor>(`${this.base}/${id}/files`, fd);
  }

  removeFile(raporId: string, fileId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${raporId}/files/${fileId}`);
  }

  async download(raporId: string, fileId: string, filename: string): Promise<void> {
    const token = this.auth.accessToken();
    if (!token) throw new Error('Oturum yok');
    const res = await fetch(`${this.base}/${raporId}/files/${fileId}/download`, {
      headers: { Authorization: `Bearer ${token}` },
      credentials: 'include',
    });
    if (!res.ok) throw new Error('İndirme başarısız');
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
  }
}
