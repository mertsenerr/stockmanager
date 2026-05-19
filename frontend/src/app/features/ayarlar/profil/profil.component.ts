import { ChangeDetectionStrategy, Component, ViewChild, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../../core/auth/auth.service';
import { ROLE_LABELS } from '../../../core/auth/auth.models';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ConfirmService } from '../../../shared/ui/confirm/confirm.service';
import { ModalComponent } from '../../../shared/ui/modal/modal.component';
import { ImageCropComponent } from './image-crop.component';

@Component({
  selector: 'app-ayarlar-profil',
  standalone: true,
  imports: [ReactiveFormsModule, ModalComponent, ImageCropComponent],
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
            <button
              type="button"
              class="profil-avatar-btn"
              (click)="triggerFilePick(fileInput)"
              [disabled]="avatarSaving()"
              [attr.aria-label]="u.avatarDataUri ? 'Profil fotoğrafını değiştir' : 'Profil fotoğrafı ekle'"
            >
              @if (u.avatarDataUri) {
                <img [src]="u.avatarDataUri" alt="" class="profil-avatar-img" />
              } @else {
                <span class="orbix-avatar profil-avatar">{{ initials() }}</span>
              }
              <span class="profil-avatar-overlay">
                {{ u.avatarDataUri ? 'Değiştir' : 'Fotoğraf ekle' }}
              </span>
            </button>
            <input #fileInput type="file" accept="image/png,image/jpeg,image/webp"
                   class="hidden-file" (change)="onFilePicked($event)" />
            <div class="profil-head-text">
              <p class="profil-head-name">{{ u.adSoyad }}</p>
              <p class="profil-head-meta">{{ u.email }} · {{ roleLabel() }}</p>
              @if (u.avatarDataUri) {
                <button type="button" class="profil-avatar-remove"
                        (click)="removeAvatar()" [disabled]="avatarSaving()">
                  Fotoğrafı kaldır
                </button>
              }
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

    <app-modal
      [open]="cropModalOpen()"
      title="Profil fotoğrafını kırp"
      size="sm"
      (closed)="closeCropModal()"
    >
      @if (rawImage(); as src) {
        <div class="crop-modal-body">
          <app-image-crop [src]="src"></app-image-crop>
          <div class="crop-modal-actions">
            <button type="button" class="btn btn-ghost" (click)="closeCropModal()" [disabled]="avatarSaving()">Vazgeç</button>
            <button type="button" class="btn btn-primary" (click)="saveCroppedAvatar()" [disabled]="avatarSaving()">
              {{ avatarSaving() ? 'Kaydediliyor…' : 'Kullan' }}
            </button>
          </div>
        </div>
      }
    </app-modal>
  `,
  styles: [`
    .ayarlar-page { display: flex; flex-direction: column; gap: 18px; }
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
      display: flex; align-items: center; gap: 16px;
      padding-bottom: 18px;
      border-bottom: 1px solid rgba(0, 0, 0, 0.06);
      margin-bottom: 18px;
    }
    :host-context([data-theme="dark"]) .profil-head {
      border-bottom-color: var(--color-border);
    }
    .profil-avatar-btn {
      position: relative;
      width: 72px; height: 72px;
      padding: 0;
      border: none;
      border-radius: 50%;
      overflow: hidden;
      background: transparent;
      cursor: pointer;
      flex-shrink: 0;
    }
    .profil-avatar-btn:disabled { cursor: not-allowed; opacity: 0.7; }
    .profil-avatar {
      width: 72px; height: 72px;
      font-size: 22px;
      border-radius: 50%;
    }
    .profil-avatar-img {
      width: 100%; height: 100%;
      object-fit: cover;
      display: block;
      border-radius: 50%;
    }
    .profil-avatar-overlay {
      position: absolute;
      inset: 0;
      display: flex; align-items: center; justify-content: center;
      background: rgba(0, 0, 0, 0.55);
      color: #fff;
      font-size: 11px;
      font-weight: 600;
      opacity: 0;
      transition: opacity 140ms;
    }
    .profil-avatar-btn:hover .profil-avatar-overlay { opacity: 1; }
    .profil-avatar-btn:focus-visible .profil-avatar-overlay { opacity: 1; }
    .profil-head-text { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
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
    .profil-avatar-remove {
      align-self: flex-start;
      margin-top: 4px;
      padding: 2px 6px;
      font-size: 11px;
      color: var(--color-coral, #e84a28);
      background: transparent;
      border: none;
      cursor: pointer;
      text-decoration: underline;
    }
    .profil-avatar-remove:disabled { opacity: 0.5; cursor: not-allowed; }
    .hidden-file { display: none; }

    .profil-form { display: flex; flex-direction: column; gap: 16px; }
    .profil-row { display: flex; flex-direction: column; gap: 6px; }
    .profil-label {
      font-family: Inter, system-ui, sans-serif;
      font-size: 12px;
      font-weight: 600;
      letter-spacing: 0.04em;
      color: var(--color-ink-secondary);
      text-transform: uppercase;
    }
    .profil-help { font-size: 11.5px; color: var(--color-ink-muted); }
    .profil-error { font-size: 11.5px; color: var(--color-coral, #e84a28); }
    .profil-actions { display: flex; gap: 8px; padding-top: 8px; }

    .crop-modal-body { display: flex; flex-direction: column; gap: 14px; }
    .crop-modal-actions {
      display: flex; justify-content: flex-end; gap: 8px;
      padding-top: 8px;
      border-top: 1px solid var(--color-border, rgba(0,0,0,0.06));
    }
  `],
})
export class ProfilComponent {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

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

  // ─── Avatar / crop ────────────────────────────────────────────────────
  @ViewChild(ImageCropComponent) private cropper?: ImageCropComponent;
  readonly cropModalOpen = signal(false);
  readonly rawImage = signal<string | null>(null);
  readonly avatarSaving = signal(false);

  triggerFilePick(input: HTMLInputElement): void {
    input.value = '';
    input.click();
  }

  onFilePicked(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) {
      this.toast.error('Sadece PNG, JPEG veya WebP yükleyebilirsiniz.');
      return;
    }
    if (file.size > 8 * 1024 * 1024) {
      this.toast.error('Resim 8 MB sınırını aşıyor. Lütfen daha küçük bir resim seçin.');
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      this.rawImage.set(reader.result as string);
      this.cropModalOpen.set(true);
    };
    reader.onerror = () => this.toast.error('Resim okunamadı.');
    reader.readAsDataURL(file);
  }

  closeCropModal(): void {
    this.cropModalOpen.set(false);
    this.rawImage.set(null);
  }

  saveCroppedAvatar(): void {
    const dataUri = this.cropper?.toDataUri('image/jpeg', 0.86);
    if (!dataUri) {
      this.toast.error('Kırpılan resim alınamadı.');
      return;
    }
    this.avatarSaving.set(true);
    this.auth.updateAvatar(dataUri).subscribe({
      next: () => {
        this.avatarSaving.set(false);
        this.toast.success('Profil fotoğrafı güncellendi.');
        this.closeCropModal();
      },
      error: (err: HttpErrorResponse) => {
        this.avatarSaving.set(false);
        this.toast.error(err.error?.message ?? 'Yükleme başarısız.');
      },
    });
  }

  async removeAvatar(): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Profil fotoğrafı silinsin mi?',
      message: 'Profil fotoğrafın kaldırılacak ve baş harflerin gösterilecek.',
      confirmLabel: 'Kaldır',
      cancelLabel: 'Vazgeç',
      danger: true,
    });
    if (!ok) return;
    this.avatarSaving.set(true);
    this.auth.removeAvatar().subscribe({
      next: () => {
        this.avatarSaving.set(false);
        this.toast.success('Profil fotoğrafı kaldırıldı.');
      },
      error: (err: HttpErrorResponse) => {
        this.avatarSaving.set(false);
        this.toast.error(err.error?.message ?? 'Silme başarısız.');
      },
    });
  }

  // ─── Ad/soyad form ────────────────────────────────────────────────────
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
