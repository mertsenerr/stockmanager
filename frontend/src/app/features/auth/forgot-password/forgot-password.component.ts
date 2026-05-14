import { ChangeDetectionStrategy, Component, ViewChild, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { AuthShellComponent } from '../auth-shell/auth-shell.component';
import { TurnstileComponent } from '../../../shared/ui/turnstile/turnstile.component';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AuthShellComponent, TurnstileComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './forgot-password.component.html',
})
export class ForgotPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  readonly submitting = signal(false);
  readonly sent = signal(false);
  readonly captchaToken = signal<string | null>(null);

  @ViewChild(TurnstileComponent) turnstile?: TurnstileComponent;
  onCaptchaToken(token: string): void { this.captchaToken.set(token); }
  onCaptchaError(): void { this.captchaToken.set(null); }

  submit(): void {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.auth.forgotPassword({ ...this.form.getRawValue(), turnstileToken: this.captchaToken() ?? undefined }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.sent.set(true);
      },
      error: () => {
        this.submitting.set(false);
        this.captchaToken.set(null);
        this.turnstile?.reset();
        // Treat as "sent" anyway — backend never reveals whether email exists.
        this.sent.set(true);
      },
    });
  }
}
