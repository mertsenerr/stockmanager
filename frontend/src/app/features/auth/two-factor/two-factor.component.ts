import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { TwoFactorMethod } from '../../../core/auth/auth.models';
import { AuthShellComponent } from '../auth-shell/auth-shell.component';
import {
  decodeRequestOptions,
  encodeAssertionResponse,
} from '../../../core/auth/webauthn.util';

interface NavState {
  pendingToken?: string;
  availableMethods?: TwoFactorMethod[];
  redirect?: string;
}

const METHOD_LABEL: Record<TwoFactorMethod, string> = {
  totp:     'Authenticator app',
  email:    'E-posta kodu',
  webauthn: 'Cihaz / Passkey',
  recovery: 'Recovery kodu',
};

@Component({
  selector: 'app-two-factor',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AuthShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-auth-shell variant="login">
      <header class="mb-7">
        <h2 class="auth-card-heading">İkinci faktör</h2>
        <p class="auth-card-subheading">Hesabını korumak için ek bir doğrulama gerekiyor.</p>
      </header>

      @if (!pendingToken()) {
        <p class="auth-server-error">Oturum bilgisi bulunamadı. Lütfen tekrar giriş yapın.</p>
        <a routerLink="/login" class="auth-link">Giriş ekranına dön</a>
      } @else {
        <!-- Method picker (one row of buttons, max 4) -->
        <div class="tf-methods" role="tablist">
          @for (m of availableMethods(); track m) {
            <button type="button" role="tab"
                    class="tf-method"
                    [class.is-active]="method() === m"
                    [attr.aria-selected]="method() === m"
                    (click)="selectMethod(m)">
              {{ label(m) }}
            </button>
          }
        </div>

        @switch (method()) {
          @case ('totp') {
            <form [formGroup]="form" (ngSubmit)="submitCode('totp')" class="space-y-5 mt-5" novalidate>
              <div>
                <label for="code-totp" class="auth-label">6 haneli kod</label>
                <input id="code-totp" inputmode="numeric" autocomplete="one-time-code"
                       formControlName="code" maxlength="6"
                       class="auth-input" placeholder="••••••" />
              </div>
              @if (serverError()) { <p class="auth-server-error">{{ serverError() }}</p> }
              <button type="submit" [disabled]="submitting() || form.invalid" class="auth-cta">Doğrula</button>
            </form>
          }
          @case ('email') {
            <div class="space-y-4 mt-5">
              <p class="text-xs text-ink-secondary">E-posta adresine 6 haneli bir kod göndereceğiz.</p>
              <button type="button" (click)="sendEmailCode()" [disabled]="emailSending()" class="btn btn-ghost">
                {{ emailSent() ? 'Tekrar gönder' : 'Kodu gönder' }}
              </button>
              @if (emailSent()) {
                <form [formGroup]="form" (ngSubmit)="submitCode('email')" class="space-y-5" novalidate>
                  <div>
                    <label for="code-email" class="auth-label">E-postadaki kod</label>
                    <input id="code-email" inputmode="numeric" autocomplete="one-time-code"
                           formControlName="code" maxlength="6" class="auth-input" placeholder="••••••" />
                  </div>
                  @if (serverError()) { <p class="auth-server-error">{{ serverError() }}</p> }
                  <button type="submit" [disabled]="submitting() || form.invalid" class="auth-cta">Doğrula</button>
                </form>
              }
            </div>
          }
          @case ('webauthn') {
            <div class="space-y-4 mt-5">
              <p class="text-xs text-ink-secondary">Cihazınla (Touch ID, Windows Hello, güvenlik anahtarı) kimliğini onayla.</p>
              <button type="button" (click)="startWebAuthn()" [disabled]="submitting()" class="auth-cta">
                {{ submitting() ? 'Bekleniyor…' : 'Cihazımla doğrula' }}
              </button>
              @if (serverError()) { <p class="auth-server-error">{{ serverError() }}</p> }
            </div>
          }
          @case ('recovery') {
            <form [formGroup]="form" (ngSubmit)="submitCode('recovery')" class="space-y-5 mt-5" novalidate>
              <div>
                <label for="code-recovery" class="auth-label">Recovery kodu</label>
                <input id="code-recovery" type="text" autocomplete="one-time-code"
                       formControlName="code" class="auth-input" placeholder="XXXX-XXXX-XXXX" />
              </div>
              @if (serverError()) { <p class="auth-server-error">{{ serverError() }}</p> }
              <button type="submit" [disabled]="submitting() || form.invalid" class="auth-cta">Doğrula</button>
            </form>
          }
        }

        <p class="text-center text-xs text-ink-secondary mt-6">
          Hata mı var? <a routerLink="/login" class="auth-link">Giriş ekranına dön</a>
        </p>
      }
    </app-auth-shell>
  `,
  styles: [`
    .tf-methods {
      display: flex; flex-wrap: wrap; gap: 6px;
      padding: 4px;
      border: 1px solid rgba(0, 0, 0, 0.06);
      border-radius: 12px;
      background: var(--color-surface-elevated, rgba(255,255,255,0.04));
    }
    .tf-method {
      flex: 1; min-width: 120px;
      padding: 8px 12px;
      border: 1px solid transparent;
      border-radius: 8px;
      background: transparent;
      cursor: pointer;
      font-family: Inter, system-ui, sans-serif;
      font-size: 12.5px; font-weight: 600;
      color: var(--color-ink-secondary);
      transition: background 140ms, border-color 140ms, color 140ms;
    }
    .tf-method:hover { color: var(--color-ink); }
    .tf-method.is-active {
      background: var(--color-surface);
      border-color: rgba(var(--color-accent-rgb), 0.30);
      color: var(--color-ink);
    }
  `],
})
export class TwoFactorComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly pendingToken = signal<string | null>(null);
  readonly availableMethods = signal<TwoFactorMethod[]>([]);
  readonly method = signal<TwoFactorMethod>('totp');
  readonly submitting = signal(false);
  readonly serverError = signal<string | null>(null);
  readonly emailSending = signal(false);
  readonly emailSent = signal(false);

  private redirect = '/';

  readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required]],
  });

  ngOnInit(): void {
    const state = (history.state ?? {}) as NavState;
    if (state.pendingToken) {
      this.pendingToken.set(state.pendingToken);
      this.availableMethods.set(state.availableMethods ?? ['totp']);
      this.method.set((state.availableMethods?.[0] as TwoFactorMethod | undefined) ?? 'totp');
      this.redirect = state.redirect ?? '/';
    }
  }

  label(m: TwoFactorMethod): string { return METHOD_LABEL[m]; }

  selectMethod(m: TwoFactorMethod): void {
    this.method.set(m);
    this.serverError.set(null);
    this.form.reset({ code: '' });
    this.emailSent.set(false);
  }

  sendEmailCode(): void {
    const tok = this.pendingToken();
    if (!tok || this.emailSending()) return;
    this.emailSending.set(true);
    this.auth.emailOtpSend(tok).subscribe({
      next: () => { this.emailSending.set(false); this.emailSent.set(true); },
      error: () => { this.emailSending.set(false); this.serverError.set('Kod gönderilemedi.'); },
    });
  }

  submitCode(method: TwoFactorMethod): void {
    const tok = this.pendingToken();
    if (!tok || this.submitting()) return;
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.submitting.set(true);
    this.serverError.set(null);
    this.auth.twoFactorVerify({
      pendingToken: tok,
      method,
      code: this.form.controls.code.value,
    }).subscribe({
      next: () => { this.submitting.set(false); this.router.navigateByUrl(this.redirect); },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.serverError.set(err.error?.message ?? 'Doğrulama başarısız.');
      },
    });
  }

  async startWebAuthn(): Promise<void> {
    const tok = this.pendingToken();
    if (!tok || this.submitting()) return;
    if (!('credentials' in navigator)) {
      this.serverError.set('Tarayıcın WebAuthn destelemiyor.');
      return;
    }
    this.submitting.set(true);
    this.serverError.set(null);
    try {
      const optsJson = await new Promise<any>((resolve, reject) =>
        this.auth.webAuthnAuthOptions(tok).subscribe({ next: resolve, error: reject }));
      const opts = decodeRequestOptions(optsJson);
      const cred = (await navigator.credentials.get({ publicKey: opts })) as PublicKeyCredential | null;
      if (!cred) throw new Error('İptal edildi.');
      const payload = encodeAssertionResponse(cred);
      this.auth.twoFactorVerify({
        pendingToken: tok, method: 'webauthn', assertionResponse: payload,
      }).subscribe({
        next: () => { this.submitting.set(false); this.router.navigateByUrl(this.redirect); },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          this.serverError.set(err.error?.message ?? 'WebAuthn doğrulaması başarısız.');
        },
      });
    } catch (e: any) {
      this.submitting.set(false);
      this.serverError.set(e?.message ?? 'WebAuthn akışı başarısız.');
    }
  }
}
