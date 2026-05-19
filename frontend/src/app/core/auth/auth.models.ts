export type UserRole = 'Sistem' | 'SayimBaskani' | 'Kullanici';

export interface CurrentUser {
  id: string;
  email: string;
  adSoyad: string;
  rol: UserRole;
  firmaId?: string | null;
  firmaAdi?: string | null;
  firmaKisaltmasi?: string | null;
  firmaIds: string[];
  magazaIds: string[];
  avatarDataUri?: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
  turnstileToken?: string;
}

export interface ForgotPasswordRequest {
  email: string;
  turnstileToken?: string;
}

export interface ResetPasswordRequest {
  token: string;
  newPassword: string;
}

export interface AuthResponse {
  accessToken: string;
  expiresInSeconds: number;
  user: CurrentUser;
}

export interface ApiValidationProblem {
  type?: string;
  title?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export const ROLE_LABELS: Record<UserRole, string> = {
  Sistem: 'Sistem Yöneticisi',
  SayimBaskani: 'Sayım Başkanı',
  Kullanici: 'Kullanıcı',
};

export interface RegisterSayimBaskaniRequest {
  email: string;
  password: string;
  adSoyad: string;
  firmaAdi: string;
  turnstileToken?: string;
}

export interface RegisterKullaniciRequest {
  email: string;
  password: string;
  adSoyad: string;
  turnstileToken?: string;
}

export interface RegisterResponse {
  message: string;
  // Backend no longer echoes the freshly created user — register response is now
  // a generic acknowledgement so /register doesn't double as an email-existence
  // oracle. The user object becomes available after email verification + login.
}

export type AuthFailureCode =
  | 'INVALID_CREDENTIALS'
  | 'EMAIL_NOT_VERIFIED'
  | 'REFRESH_INVALID'
  | 'ACCOUNT_LOCKED';

export interface AuthFailureBody {
  message: string;
  code?: AuthFailureCode;
  /** Populated when code = ACCOUNT_LOCKED — seconds until the next try is allowed. */
  retryAfterSeconds?: number;
}

/** Body sent with every 2FA enroll/disable/regen call. Backend requires the
 * caller's current password and, if any 2FA factor is enabled, a fresh proof
 * for one of them — so a hijacked session can't silently swap the second factor
 * out from under the real owner. */
export interface TwoFactorStepUpProof {
  currentPassword: string;
  twoFactorMethod?: 'totp' | 'email' | 'recovery';
  twoFactorCode?: string;
}

export interface VerifyEmailRequest {
  token: string;
}

export interface ResendVerificationRequest {
  email: string;
}

export interface UpdateProfileRequest {
  adSoyad: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  /** Required when the user has any 2FA method enabled. */
  twoFactorMethod?: 'totp' | 'email' | 'recovery';
  twoFactorCode?: string;
  turnstileToken?: string;
}

export interface RevokeOtherSessionsResponse {
  revoked: number;
}

export interface ActiveSession {
  id: string;
  browser: string;
  os: string;
  ip?: string | null;
  createdAt: string;
  expiresAt: string;
  isCurrent: boolean;
}

// ─── Two-factor authentication ───────────────────────────────────────────────
export type TwoFactorMethod = 'totp' | 'email' | 'webauthn' | 'recovery';

export interface TwoFactorRequiredResponse {
  requiresTwoFactor: true;
  pendingToken: string;
  availableMethods: TwoFactorMethod[];
}

export type LoginOutcome = AuthResponse | TwoFactorRequiredResponse;

export function isTwoFactorRequired(r: LoginOutcome): r is TwoFactorRequiredResponse {
  return (r as TwoFactorRequiredResponse).requiresTwoFactor === true;
}

export interface TwoFactorStatus {
  totpEnabled: boolean;
  emailOtpEnabled: boolean;
  webAuthnEnabled: boolean;
  webAuthnCredentialCount: number;
  recoveryCodesRemaining: number;
}

export interface TotpSetupResponse {
  secret: string;
  otpAuthUrl: string;
  qrPngDataUri: string;
}

export interface RecoveryCodesResponse {
  codes: string[];
}

export interface WebAuthnCredentialDto {
  id: string;
  nickname?: string | null;
  createdAt: string;
}

export interface TwoFactorVerifyRequest {
  pendingToken: string;
  method: TwoFactorMethod;
  code?: string;
  assertionResponse?: unknown; // Raw WebAuthn assertion JSON.
}
