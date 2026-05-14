import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, share, tap, throwError, timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ActiveSession,
  AuthResponse,
  ChangePasswordRequest,
  CurrentUser,
  ForgotPasswordRequest,
  LoginOutcome,
  LoginRequest,
  RecoveryCodesResponse,
  RegisterKullaniciRequest,
  RegisterResponse,
  RegisterSayimBaskaniRequest,
  ResendVerificationRequest,
  ResetPasswordRequest,
  RevokeOtherSessionsResponse,
  TotpSetupResponse,
  TwoFactorStatus,
  TwoFactorVerifyRequest,
  UpdateProfileRequest,
  VerifyEmailRequest,
  WebAuthnCredentialDto,
  isTwoFactorRequired,
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

  login(request: LoginRequest): Observable<LoginOutcome> {
    return this.http
      .post<LoginOutcome>(`${this.base}/login`, request, { withCredentials: true })
      .pipe(
        timeout({ each: 30000 }),
        tap((res) => { if (!isTwoFactorRequired(res)) this.applyAuth(res); }),
      );
  }

  // ─── Two-factor authentication ───────────────────────────────────────────
  twoFactorStatus(): Observable<TwoFactorStatus> {
    return this.http.get<TwoFactorStatus>(`${this.base}/2fa/status`, { withCredentials: true });
  }

  totpSetup(): Observable<TotpSetupResponse> {
    return this.http.post<TotpSetupResponse>(`${this.base}/2fa/totp/setup`, {}, { withCredentials: true });
  }

  totpEnable(code: string): Observable<RecoveryCodesResponse> {
    return this.http.post<RecoveryCodesResponse>(`${this.base}/2fa/totp/enable`, { code }, { withCredentials: true });
  }

  totpDisable(): Observable<void> {
    return this.http.post<void>(`${this.base}/2fa/totp/disable`, {}, { withCredentials: true });
  }

  emailOtpEnable(): Observable<void> {
    return this.http.post<void>(`${this.base}/2fa/email/enable`, {}, { withCredentials: true });
  }

  emailOtpDisable(): Observable<void> {
    return this.http.post<void>(`${this.base}/2fa/email/disable`, {}, { withCredentials: true });
  }

  emailOtpSend(pendingToken: string): Observable<{ sent: boolean }> {
    return this.http.post<{ sent: boolean }>(`${this.base}/2fa/email/send`, { pendingToken });
  }

  webAuthnList(): Observable<WebAuthnCredentialDto[]> {
    return this.http.get<WebAuthnCredentialDto[]>(`${this.base}/2fa/webauthn`, { withCredentials: true });
  }

  webAuthnRegisterOptions(): Observable<unknown> {
    return this.http.post<unknown>(`${this.base}/2fa/webauthn/register/options`, {}, { withCredentials: true });
  }

  webAuthnRegisterComplete(response: unknown, nickname?: string): Observable<{ id: string; nickname?: string }> {
    return this.http.post<{ id: string; nickname?: string }>(
      `${this.base}/2fa/webauthn/register/complete`,
      { response, nickname }, { withCredentials: true });
  }

  webAuthnDelete(credentialId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/2fa/webauthn/${encodeURIComponent(credentialId)}`, { withCredentials: true });
  }

  webAuthnAuthOptions(pendingToken: string): Observable<unknown> {
    return this.http.post<unknown>(`${this.base}/2fa/webauthn/auth/options`, { pendingToken });
  }

  regenerateRecoveryCodes(): Observable<RecoveryCodesResponse> {
    return this.http.post<RecoveryCodesResponse>(`${this.base}/2fa/recovery-codes/regenerate`, {}, { withCredentials: true });
  }

  twoFactorVerify(req: TwoFactorVerifyRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${this.base}/2fa/verify`, req, { withCredentials: true })
      .pipe(tap((res) => this.applyAuth(res)));
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

  verifyEmail(req: VerifyEmailRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.base}/verify-email`, req);
  }

  resendVerification(req: ResendVerificationRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.base}/resend-verification`, req);
  }

  captchaConfig(): Observable<{ enabled: boolean; siteKey: string }> {
    return this.http.get<{ enabled: boolean; siteKey: string }>(`${this.base}/captcha/config`);
  }

  updateProfile(req: UpdateProfileRequest): Observable<CurrentUser> {
    return this.http
      .patch<CurrentUser>(`${this.base}/me`, req, { withCredentials: true })
      .pipe(tap((user) => this._currentUser.set(user)));
  }

  changePassword(req: ChangePasswordRequest): Observable<void> {
    return this.http
      .post<void>(`${this.base}/change-password`, req, { withCredentials: true });
  }

  revokeOtherSessions(): Observable<RevokeOtherSessionsResponse> {
    return this.http
      .post<RevokeOtherSessionsResponse>(`${this.base}/sessions/revoke-others`, {}, { withCredentials: true });
  }

  listSessions(): Observable<ActiveSession[]> {
    return this.http.get<ActiveSession[]>(`${this.base}/sessions`, { withCredentials: true });
  }

  revokeSession(sessionId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/sessions/${encodeURIComponent(sessionId)}`, { withCredentials: true });
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
