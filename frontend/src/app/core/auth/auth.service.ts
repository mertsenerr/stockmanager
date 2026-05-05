import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, share, tap, throwError, timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AuthResponse,
  CurrentUser,
  ForgotPasswordRequest,
  LoginRequest,
  RegisterKullaniciRequest,
  RegisterResponse,
  RegisterSayimBaskaniRequest,
  ResetPasswordRequest,
} from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/auth`;

  private readonly _accessToken = signal<string | null>(null);
  private readonly _currentUser = signal<CurrentUser | null>(null);
  private readonly _bootstrapping = signal(true);

  readonly currentUser = this._currentUser.asReadonly();
  readonly accessToken = this._accessToken.asReadonly();
  readonly bootstrapping = this._bootstrapping.asReadonly();
  readonly isAuthenticated = computed(() => this._currentUser() !== null);

  private inFlightRefresh: Observable<AuthResponse | null> | null = null;

  bootstrap(): Observable<CurrentUser | null> {
    return this.refresh().pipe(
      map((res) => res?.user ?? null),
      tap(() => this._bootstrapping.set(false)),
      catchError(() => {
        this._bootstrapping.set(false);
        return of(null);
      }),
    );
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${this.base}/login`, request, { withCredentials: true })
      .pipe(timeout({ each: 30000 }), tap((res) => this.applyAuth(res)));
  }

  logout(): Observable<void> {
    return this.http
      .post<void>(`${this.base}/logout`, {}, { withCredentials: true })
      .pipe(
        tap(() => this.clearAuth()),
        catchError(() => {
          this.clearAuth();
          return of(void 0);
        }),
      );
  }

  refresh(): Observable<AuthResponse | null> {
    if (this.inFlightRefresh) return this.inFlightRefresh;

    // Render free-tier cold start veya backend 500'ü kullanıcıyı boş sayfada
    // bekletmesin: 8sn'de yanıt yoksa "anonim" devam et.
    this.inFlightRefresh = this.http
      .post<AuthResponse>(`${this.base}/refresh`, {}, { withCredentials: true })
      .pipe(
        timeout({ each: 8000 }),
        tap((res) => this.applyAuth(res)),
        map((res) => res as AuthResponse | null),
        catchError(() => {
          this.clearAuth();
          return of(null);
        }),
        tap(() => (this.inFlightRefresh = null)),
        share(),
      );

    return this.inFlightRefresh;
  }

  forgotPassword(req: ForgotPasswordRequest): Observable<void> {
    return this.http
      .post<{ message: string }>(`${this.base}/forgot-password`, req)
      .pipe(map(() => void 0));
  }

  resetPassword(req: ResetPasswordRequest): Observable<void> {
    return this.http.post<{ message: string }>(`${this.base}/reset-password`, req).pipe(
      map(() => void 0),
      catchError((err) => throwError(() => err)),
    );
  }

  registerSayimBaskani(req: RegisterSayimBaskaniRequest): Observable<RegisterResponse> {
    return this.http
      .post<RegisterResponse>(`${this.base}/register/sayim-baskani`, req)
      .pipe(timeout({ each: 30000 }));
  }

  registerKullanici(req: RegisterKullaniciRequest): Observable<RegisterResponse> {
    return this.http
      .post<RegisterResponse>(`${this.base}/register/kullanici`, req)
      .pipe(timeout({ each: 30000 }));
  }

  hasRole(...roles: CurrentUser['rol'][]): boolean {
    const user = this._currentUser();
    return user !== null && roles.includes(user.rol);
  }

  private applyAuth(res: AuthResponse): void {
    this._accessToken.set(res.accessToken);
    this._currentUser.set(res.user);
  }

  private clearAuth(): void {
    this._accessToken.set(null);
    this._currentUser.set(null);
  }
}
