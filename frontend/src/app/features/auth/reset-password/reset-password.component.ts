import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiValidationProblem } from '../../../core/auth/auth.models';
import { AuthShellComponent } from '../auth-shell/auth-shell.component';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AuthShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reset-password.component.html',
})
export class ResetPasswordComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly form = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', [Validators.required]],
  });

  readonly token = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly success = signal(false);
  readonly serverError = signal<string | null>(null);
  readonly fieldErrors = signal<Record<string, string[]>>({});

  ngOnInit(): void {
    const t = this.route.snapshot.queryParamMap.get('token');
    this.token.set(t);
  }

  submit(): void {
    if (this.submitting()) return;
    this.serverError.set(null);
    this.fieldErrors.set({});

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (this.form.controls.newPassword.value !== this.form.controls.confirmPassword.value) {
      this.serverError.set('Parolalar eşleşmiyor.');
      return;
    }
    const t = this.token();
    if (!t) {
      this.serverError.set('Token eksik. Lütfen sıfırlama bağlantısını yeniden iste.');
      return;
    }

    this.submitting.set(true);
    this.auth
      .resetPassword({ token: t, newPassword: this.form.controls.newPassword.value })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.success.set(true);
          setTimeout(() => this.router.navigate(['/login']), 1500);
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          if (err.status === 400 && err.error) {
            const problem = err.error as ApiValidationProblem;
            if (problem.errors) {
              this.fieldErrors.set(problem.errors);
              return;
            }
            this.serverError.set(err.error.message ?? 'Token geçersiz.');
          } else {
            this.serverError.set('Beklenmeyen hata.');
          }
        },
      });
  }
}
