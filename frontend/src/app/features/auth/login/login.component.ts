import { ChangeDetectionStrategy, Component, ViewChild, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/auth/auth.service';
import {
  ApiValidationProblem,
  AuthFailureBody,
  AuthFailureCode,
  isTwoFactorRequired,
} from '../../../core/auth/auth.models';
import { AuthShellComponent } from '../auth-shell/auth-shell.component';
import { TurnstileComponent } from '../../../shared/ui/turnstile/turnstile.component';

/** localStorage key for the remembered email + 30-day expiry timestamp. */
const REMEMBERED_EMAIL_KEY = 'syncompare.rememberedEmail';
const REMEMBER_MAX_AGE_MS = 30 * 24 * 60 * 60 * 1000;

interface RememberedEmail {
  email: string;
  savedAt: number;
}

function readRememberedEmail(): string | null {
  try {
    const raw = localStorage.getItem(REMEMBERED_EMAIL_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as RememberedEmail;
    if (Date.now() - parsed.savedAt > REMEMBER_MAX_AGE_MS) {
      localStorage.removeItem(REMEMBERED_EMAIL_KEY);
      return null;
    }
    return parsed.email || null;
  } catch {
    return null;
  }
}

function writeRememberedEmail(email: string): void {
  try {
    const payload: RememberedEmail = { email, savedAt: Date.now() };
    localStorage.setItem(REMEMBERED_EMAIL_KEY, JSON.stringify(payload));
  } catch {
    // Storage unavailable (private mode quota etc.) — silently ignore.
  }
}

function clearRememberedEmail(): void {
  try { localStorage.removeItem(REMEMBERED_EMAIL_KEY); } catch {}
}

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AuthShellComponent, TurnstileComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.component.html',
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly rememberedEmail = readRememberedEmail();

  readonly form = this.fb.nonNullable.group({
    email: [this.rememberedEmail ?? '', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
    rememberMe: [this.rememberedEmail !== null],
  });

  readonly hasRememberedEmail = signal<boolean>(this.rememberedEmail !== null);
  readonly captchaToken = signal<string | null>(null);

  @ViewChild(TurnstileComponent) turnstile?: TurnstileComponent;

  onCaptchaToken(token: string): void { this.captchaToken.set(token); }
  onCaptchaError(): void { this.captchaToken.set(null); }

  forgetEmail(): void {
    clearRememberedEmail();
    this.hasRememberedEmail.set(false);
    this.form.patchValue({ email: '', rememberMe: false });
  }

  readonly submitting = signal(false);
  readonly serverError = signal<string | null>(null);
  readonly errorCode = signal<AuthFailureCode | null>(null);
  readonly fieldErrors = signal<Record<string, string[]>>({});
  readonly resendSubmitting = signal(false);
  readonly resendDone = signal(false);

  submit(): void {
    if (this.submitting()) return;
    this.serverError.set(null);
    this.errorCode.set(null);
    this.resendDone.set(false);
    this.fieldErrors.set({});

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const payload = { ...this.form.getRawValue(), turnstileToken: this.captchaToken() ?? undefined };
    this.auth.login(payload).subscribe({
      next: (res) => {
        this.submitting.set(false);
        // Persist email locally for next visit only when user opted in via the
        // "Beni 30 gün hatırla" checkbox. Toggling off explicitly forgets it.
        if (payload.rememberMe) {
          writeRememberedEmail(payload.email);
        } else {
          clearRememberedEmail();
        }

        if (isTwoFactorRequired(res)) {
          // Hand off the pending token to the 2FA verification screen via router state —
          // we don't put it in the URL because it's a short-lived bearer.
          this.router.navigate(['/two-factor'], {
            state: {
              pendingToken: res.pendingToken,
              availableMethods: res.availableMethods,
              redirect: new URLSearchParams(window.location.search).get('redirect') ?? '/',
            },
          });
          return;
        }

        const redirect = new URLSearchParams(window.location.search).get('redirect') ?? '/';
        this.router.navigateByUrl(redirect);
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        // Reset captcha so the user can try again with a fresh token.
        this.captchaToken.set(null);
        this.turnstile?.reset();
        if (err.status === 400 && err.error) {
          const problem = err.error as ApiValidationProblem;
          if (problem.errors) {
            this.fieldErrors.set(problem.errors);
            return;
          }
        }
        const body = err.error as AuthFailureBody | undefined;
        this.errorCode.set(body?.code ?? null);
        this.serverError.set(body?.message ?? 'Giriş başarısız. Lütfen tekrar dene.');
      },
    });
  }

  resendVerification(): void {
    if (this.resendSubmitting()) return;
    const email = this.form.controls.email.value?.trim();
    if (!email) return;
    this.resendSubmitting.set(true);
    this.auth.resendVerification({ email }).subscribe({
      next: () => {
        this.resendSubmitting.set(false);
        this.resendDone.set(true);
      },
      error: () => {
        // Match backend's "always 200" semantics on the UI side — never reveal failure.
        this.resendSubmitting.set(false);
        this.resendDone.set(true);
      },
    });
  }
}
