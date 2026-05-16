import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface HealthCheckResponse {
  status: 'ok' | 'degraded' | 'unreachable';
}

@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);

  check(): Observable<HealthCheckResponse> {
    return this.http
      .get<HealthCheckResponse>(`${environment.apiBaseUrl}/api/health`)
      .pipe(
        catchError(() => of<HealthCheckResponse>({ status: 'unreachable' })),
      );
  }
}
