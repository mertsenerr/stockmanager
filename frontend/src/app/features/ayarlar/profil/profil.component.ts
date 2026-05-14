import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../../core/auth/auth.service';
import { ROLE_LABELS } from '../../../core/auth/auth.models';
import { ToastService } from '../../../shared/ui/toast/toast.service';

@Component({
  selector: 'app-ayarlar-profil',
  standalone: true,
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="ayarlar-page">
      <header class="ayarlar-page-head">
        <h2 class="ayarlar-page-title">Profilim</h2>
        <p class="ayarlar-page-desc">Görünen adınızı buradan güncelleyebilirsiniz. E-posta ve rol değişiklikleri yöneticiniz tarafından yapılır.</p>
      </header>

      @if (user(); as u) {
        <section class="ayarlar-card">
          <div class="profil-head">
            <span class="orbix-avatar profil-avatar">{{ initials() }}</span>
            <div>
              <p class="profil-head-name">{{ u.adSoyad }}</p>
              <p class="profil-head-meta">{{ u.email }} · {{ roleLabel() }}</p>
            </div>
          </div>

          <form [formGroup]="form" (ngSubmit)="submit()" class="profil-form" novalidate>
            <div class="profil-row">
              <label for="adSoyad" class="profil-label">Ad Soyad</label>
              <input
                id="adSoyad"
                type="text"
                formControlName="adSoyad"
                class="field-input"
                autocomplete="name"
                placeholder="Ad Soyad"
              />
              @if (form.controls.adSoyad.touched && form.controls.adSoyad.errors) {
                <p class="profil-error">
                  @if (form.controls.adSoyad.errors['required']) { Ad soyad zorunludur. }
                  @else if (form.controls.adSoyad.errors['minlength']) { En az 2 karakter olmalı. }
                  @else if (form.controls.adSoyad.errors['maxlength']) { En fazla 120 karakter olabilir. }
                </p>
              }
              @for (msg of fieldErrors()['AdSoyad']; track msg) {
                <p class="profil-error">{{ msg }}</p>
              }
            </div>

            <div class="profil-row">
              <label class="profil-label">E-posta</label>
              <input type="email" [value]="u.email" disabled class="field-input" />
              <p class="profil-help">E-posta değişikliği için sistem yöneticinize başvurun.</p>
            </div>

            <div class="profil-row">
              <label class="profil-label">Rol</label>
              <input type="text" [value]="roleLabel()" disabled class="field-input" />
            </div>

            <div class="profil-actions">
              <button type="submit" [disabled]="saving() || form.invalid || !form.dirty" class="btn btn-primary">
                {{ saving() ? 'Kaydediliyor…' : 'Kaydet' }}
              </button>
              @if (form.dirty && !saving()) {
                <button type="button" (click)="resetForm()" class="btn btn-ghost">Vazgeç</button>
              }
            </div>
          </form>
        </section>
      }
    </article>
  `,
  styles: [`
    .ayarlar-page {
      display: flex; flex-direction: column; gap: 18px;
    }
    .ayarlar-page-head { display: flex; flex-direction: column; gap: 4px; }
    .ayarlar-page-title {
      font-family: Inter, system-ui, sans-serif;
      font-size: 24px;
      font-weight: 700;
      letter-spacing: -0.02em;
      color: var(--color-ink);
      line-height: 1.1;
    }
    .ayarlar-page-desc {
      font-size: 13px;
      color: var(--color-ink-secondary);
      max-width: 56ch;
    }
    .ayarlar-card {
      padding: 22px;
      border-radius: 14px;
      border: 1px solid rgba(0, 0, 0, 0.06);
      background: var(--color-surface);
    }
    :host-context([data-theme="dark"]) .ayarlar-card {
      border-color: var(--color-border);
    }
    .profil-head {
      display: flex; align-items: center; gap: 14px;
      padding-bottom: 18px;
      border-bottom: 1px solid rgba(0, 0, 0, 0.06);
      margin-bottom: 18px;
    }
    :host-context([data-theme="dark"]) .profil-head {
      border-bottom-color: var(--color-border);
    }
    .profil-avatar {
      width: 56px; height: 56px;
      font-size: 18px;
      border-radius: 14px;
    }
    .profil-head-name {
      font-family: Inter, system-ui, sans-serif;
      font-size: 16px;
      font-weight: 700;
      color: var(--color-ink);
    }
    .profil-head-meta {
      font-size: 12px;
      color: var(--color-ink-muted);
    }
    .profil-form {
      display: flex; flex-direction: column; gap: 16px;
    }
    .profil-row {
      display: flex; flex-direction: column; gap: 6px;
    }
    .profil-label {
      font-family: Inter, system-ui, sans-serif;
      font-size: 12px;
      font-weight: 600;
      letter-spacing: 0.04em;
      color: var(--color-ink-secondary);
      text-transform: uppercase;
    }
    .profil-help {
      font-size: 11.5px;
      color: var(--color-ink-muted);
    }
    .profil-error {
      font-size: 11.5px;
      color: var(--color-coral, #e84a28);
    }
    .profil-actions {
      display: flex; gap: 8px;
      padding-top: 8px;
    }
  `],
})
export class ProfilComponent {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(ToastService);

  readonly user = this.auth.currentUser;
  readonly roleLabel = computed(() => {
    const u = this.user();
    return u ? ROLE_LABELS[u.rol] : '';
  });
  readonly initials = computed(() => {
    const u = this.user();
    if (!u) return '··';
    return u.adSoyad
      .split(/\s+/).filter(Boolean).slice(0, 2)
      .map((s) => s[0]?.toUpperCase() ?? '').join('') || '··';
  });

  readonly form = this.fb.nonNullable.group({
    adSoyad: [this.user()?.adSoyad ?? '', [Validators.required, Validators.minLength(2), Validators.maxLength(120)]],
  });

  readonly saving = signal(false);
  readonly fieldErrors = signal<Record<string, string[]>>({});

  resetForm(): void {
    this.form.reset({ adSoyad: this.user()?.adSoyad ?? '' });
    this.fieldErrors.set({});
  }

  submit(): void {
    if (this.saving() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.fieldErrors.set({});
    this.saving.set(true);
    this.auth.updateProfile(this.form.getRawValue()).subscribe({
      next: () => {
        this.saving.set(false);
        this.form.markAsPristine();
        this.toast.success('Profil güncellendi.');
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        if (err.status === 400 && err.error?.errors) {
          this.fieldErrors.set(err.error.errors);
        } else {
          this.toast.error(err.error?.message ?? 'Güncelleme başarısız oldu.');
        }
      },
    });
  }
}
