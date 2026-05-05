import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiValidationProblem } from '../../../core/auth/auth.models';
import { AuthShellComponent } from '../auth-shell/auth-shell.component';

type Tab = 'baskani' | 'kullanici';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AuthShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './register.component.html',
})
export class RegisterComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly tab = signal<Tab>('baskani');
  readonly submitting = signal(false);
  readonly serverError = signal<string | null>(null);
  readonly fieldErrors = signal<Record<string, string[]>>({});
  readonly successMessage = signal<string | null>(null);

  readonly baskaniForm = this.fb.nonNullable.group({
    adSoyad: ['', [Validators.required, Validators.maxLength(120)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    firmaAdi: ['', [Validators.required, Validators.maxLength(120)]],
    firmaKisaltmasi: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9]{3,6}$/)]],
  });

  readonly kullaniciForm = this.fb.nonNullable.group({
    adSoyad: ['', [Validators.required, Validators.maxLength(120)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    firmaKisaltmasi: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9]{3,6}$/)]],
  });

  setTab(t: Tab): void {
    this.tab.set(t);
    this.serverError.set(null);
    this.fieldErrors.set({});
    this.successMessage.set(null);
  }

  submit(): void {
    if (this.submitting()) return;
    this.serverError.set(null);
    this.fieldErrors.set({});

    if (this.tab() === 'baskani') {
      if (this.baskaniForm.invalid) {
        this.baskaniForm.markAllAsTouched();
        return;
      }
      this.submitting.set(true);
      this.auth.registerSayimBaskani(this.baskaniForm.getRawValue()).subscribe({
        next: () => {
          this.submitting.set(false);
          this.successMessage.set('Kayıt tamamlandı. Giriş sayfasına yönlendiriliyorsun…');
          setTimeout(() => this.router.navigateByUrl('/login'), 1500);
        },
        error: (err: HttpErrorResponse) => this.handleError(err),
      });
    } else {
      if (this.kullaniciForm.invalid) {
        this.kullaniciForm.markAllAsTouched();
        return;
      }
      this.submitting.set(true);
      this.auth.registerKullanici(this.kullaniciForm.getRawValue()).subscribe({
        next: () => {
          this.submitting.set(false);
          this.successMessage.set('Kayıt alındı. Sayım Başkanı onayından sonra giriş yapabilirsin.');
        },
        error: (err: HttpErrorResponse) => this.handleError(err),
      });
    }
  }

  private handleError(err: HttpErrorResponse): void {
    this.submitting.set(false);
    if (err.status === 400 && err.error) {
      const problem = err.error as ApiValidationProblem;
      this.fieldErrors.set(problem.errors ?? {});
      return;
    }
    this.serverError.set(err.error?.message ?? 'Kayıt başarısız. Lütfen tekrar dene.');
  }
}
