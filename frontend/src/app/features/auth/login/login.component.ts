import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/auth/auth.service';
import {
  ApiValidationProblem,
  AuthFailureBody,
  AuthFailureCode,
} from '../../../core/auth/auth.models';
import { AuthShellComponent } from '../auth-shell/auth-shell.component';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AuthShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.component.html',
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
    rememberMe: [false],
  });

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
    this.auth.login(this.form.getRawValue()).subscribe({
      next: () => {
        this.submitting.set(false);
        const redirect = new URLSearchParams(window.location.search).get('redirect') ?? '/';
        this.router.navigateByUrl(redirect);
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        if (err.status === 400 && err.error) {
          const problem = err.error as ApiValidationProblem;
          this.fieldErrors.set(problem.errors ?? {});
          return;
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
