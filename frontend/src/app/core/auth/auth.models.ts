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
  onayli: boolean;
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface ForgotPasswordRequest {
  email: string;
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
  firmaKisaltmasi: string;
}

export interface RegisterKullaniciRequest {
  email: string;
  password: string;
  adSoyad: string;
  firmaKisaltmasi: string;
}

export interface RegisterResponse {
  message: string;
  user: CurrentUser;
}

export type AuthFailureCode =
  | 'INVALID_CREDENTIALS'
  | 'EMAIL_NOT_VERIFIED'
  | 'NOT_APPROVED'
  | 'REFRESH_INVALID';

export interface AuthFailureBody {
  message: string;
  code?: AuthFailureCode;
}

export interface VerifyEmailRequest {
  token: string;
}

export interface ResendVerificationRequest {
  email: string;
}
