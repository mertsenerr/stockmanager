import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/auth/auth.service';
import { AuthShellComponent } from '../auth-shell/auth-shell.component';

@Component({
  selector: 'app-password-change-undo',
  standalone: true,
  imports: [RouterLink, AuthShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-auth-shell variant="reset">
      <header class="mb-7">
        <h2 class="auth-card-heading">Parola değişikliğini geri al</h2>
        <p class="auth-card-subheading">Hesabının parolası az önce değiştirildi. Bu sen değilsen aşağıdaki butona basarak eski parolanı geri yükleyebilirsin.</p>
      </header>

      @if (state() === 'pending' && token()) {
        <button type="button" (click)="undo()" [disabled]="submitting()" class="auth-cta">
          {{ submitting() ? 'Geri alınıyor…' : 'Bu ben değildim — geri al' }}
        </button>
        <p class="text-center text-xs text-ink-secondary mt-6">
          Bu işlemi sen yaptıysan bu sayfayı kapatabilirsin.
        </p>
      } @else if (state() === 'success') {
        <div class="auth-server-error" style="background: rgba(20, 184, 166, 0.10); border-color: rgba(20, 184, 166, 0.40); color: var(--color-accent, #14b8a6);">
          {{ message() }}
        </div>
        <a routerLink="/login" class="auth-cta mt-4">Giriş ekranına git</a>
      } @else if (state() === 'error') {
        <div class="auth-server-error">{{ message() }}</div>
        <a routerLink="/login" class="auth-link mt-4 block text-center">Giriş ekranına dön</a>
      } @else {
        <div class="auth-server-error">Geri alma bağlantısı bulunamadı. Lütfen e-postandaki linki tekrar tıkla.</div>
        <a routerLink="/login" class="auth-link mt-4 block text-center">Giriş ekranına dön</a>
      }
    </app-auth-shell>
  `,
})
export class PasswordChangeUndoComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);

  readonly token = signal<string | null>(null);
  readonly state = signal<'pending' | 'success' | 'error' | 'no-token'>('pending');
  readonly message = signal<string>('');
  readonly submitting = signal(false);

  ngOnInit(): void {
    const t = this.route.snapshot.queryParamMap.get('token');
    if (!t) {
      this.state.set('no-token');
      return;
    }
    this.token.set(t);
  }

  undo(): void {
    const t = this.token();
    if (!t || this.submitting()) return;
    this.submitting.set(true);
    this.auth.undoPasswordChange(t).subscribe({
      next: (res) => {
        this.submitting.set(false);
        this.state.set('success');
        this.message.set(res.message);
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.state.set('error');
        this.message.set(err.error?.message ?? 'Geri alma başarısız oldu.');
      },
    });
  }
}
