import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface HealthCheckResponse {
  status: 'healthy' | 'degraded' | 'unreachable';
  service?: string;
  version?: string;
  timestamp?: string;
  checks?: { mongo?: 'up' | 'down' };
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
