import { Injectable, signal } from '@angular/core';
import { TwoFactorStepUpProof } from '../../../core/auth/auth.models';

export type StepUpMethod = 'totp' | 'email' | 'recovery';

export interface StepUpAvailableMethod {
  value: StepUpMethod;
  label: string;
}

export interface StepUpRequest {
  /** Modal başlığı — örn. "TOTP'u kapat" / "Recovery kodlarını yenile". */
  title: string;
  /** Açıklama metni — yapılacak işlemin sonucunu kullanıcıya hatırlat. */
  message: string;
  /** Hesapta aktif 2FA yöntemleri varsa burada listele. Boş geçilirse
   *  modal sadece parola sorar. */
  twoFactorMethods?: StepUpAvailableMethod[];
  confirmLabel?: string;
  cancelLabel?: string;
  /** Yıkıcı eylem (disable/delete/regenerate) — buton kırmızı render edilir. */
  danger?: boolean;
}

interface InternalRequest extends StepUpRequest {
  resolve: (proof: TwoFactorStepUpProof | null) => void;
}

@Injectable({ providedIn: 'root' })
export class StepUpService {
  readonly current = signal<InternalRequest | null>(null);

  /** Kullanıcıya parola (+ varsa 2FA) sor; iptal ederse null döner. */
  ask(opts: StepUpRequest): Promise<TwoFactorStepUpProof | null> {
    return new Promise((resolve) => {
      this.current.set({ ...opts, resolve });
    });
  }

  submit(proof: TwoFactorStepUpProof): void {
    const req = this.current();
    if (!req) return;
    req.resolve(proof);
    this.current.set(null);
  }

  cancel(): void {
    const req = this.current();
    if (!req) return;
    req.resolve(null);
    this.current.set(null);
  }
}
