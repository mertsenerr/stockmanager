import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../../core/auth/auth.service';
import { ActiveSession, TotpSetupResponse, TwoFactorStatus, WebAuthnCredentialDto } from '../../../core/auth/auth.models';
import {
  decodeCreationOptions,
  encodeAttestationResponse,
} from '../../../core/auth/webauthn.util';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ConfirmService } from '../../../shared/ui/confirm/confirm.service';

@Component({
  selector: 'app-ayarlar-guvenlik',
  standalone: true,
  imports: [ReactiveFormsModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="ayarlar-page">
      <header class="ayarlar-page-head">
        <h2 class="ayarlar-page-title">Gizlilik & güvenlik</h2>
        <p class="ayarlar-page-desc">Hesabını koruyan kontroller. Şüpheli bir aktivite görürsen önce parolanı değiştir, sonra diğer cihazları sonlandır.</p>
      </header>

      <!-- Şifre değiştir -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head">
          <h3 class="ayarlar-card-title">Parolayı değiştir</h3>
          <p class="ayarlar-card-desc">Yeni parolan en az 8 karakter olmalı. Değişiklik sonrası bu cihaz açık kalır, diğer tüm cihazlardan otomatik çıkış yapılır.</p>
        </header>

        <form [formGroup]="form" (ngSubmit)="submit()" class="guv-form" novalidate>
          <div class="guv-row">
            <label for="currentPassword" class="guv-label">Mevcut parola</label>
            <input id="currentPassword" type="password" formControlName="currentPassword"
                   class="field-input" autocomplete="current-password" />
            @if (form.controls.currentPassword.touched && form.controls.currentPassword.errors?.['required']) {
              <p class="guv-error">Mevcut parola zorunludur.</p>
            }
          </div>

          <div class="guv-row">
            <label for="newPassword" class="guv-label">Yeni parola</label>
            <input id="newPassword" type="password" formControlName="newPassword"
                   class="field-input" autocomplete="new-password" />
            @if (form.controls.newPassword.touched && form.controls.newPassword.errors) {
              <p class="guv-error">
                @if (form.controls.newPassword.errors['required']) { Yeni parola zorunludur. }
                @else if (form.controls.newPassword.errors['minlength']) { En az 8 karakter olmalı. }
              </p>
            }
          </div>

          <div class="guv-row">
            <label for="confirmPassword" class="guv-label">Yeni parola (tekrar)</label>
            <input id="confirmPassword" type="password" formControlName="confirmPassword"
                   class="field-input" autocomplete="new-password" />
            @if (form.controls.confirmPassword.touched && passwordMismatch()) {
              <p class="guv-error">Parolalar eşleşmiyor.</p>
            }
          </div>

          @if (serverError()) {
            <p class="guv-error">{{ serverError() }}</p>
          }

          <div class="guv-actions">
            <button type="submit" [disabled]="saving() || form.invalid || passwordMismatch()" class="btn btn-primary">
              {{ saving() ? 'Değiştiriliyor…' : 'Parolayı değiştir' }}
            </button>
          </div>
        </form>
      </section>

      <!-- Aktif oturumlar -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head ayarlar-card-head-row">
          <div>
            <h3 class="ayarlar-card-title">Aktif oturumlar</h3>
            <p class="ayarlar-card-desc">Hesabın açık olan tüm cihazlar. Tanımadığın bir cihaz görüyorsan oturumunu sonlandır ve parolanı değiştir.</p>
          </div>
          <button type="button" (click)="refreshSessions()" [disabled]="sessionsLoading()" class="btn btn-ghost btn-sm">Yenile</button>
        </header>

        @if (sessionsLoading() && sessions().length === 0) {
          <p class="text-xs text-ink-muted text-center py-4">Yükleniyor…</p>
        } @else if (sessions().length === 0) {
          <p class="text-xs text-ink-muted text-center py-4">Hiç aktif oturum yok.</p>
        } @else {
          <ul class="guv-session-list">
            @for (s of sessions(); track s.id) {
              <li class="guv-session-row">
                <div class="guv-session-info">
                  <p class="guv-session-name">{{ s.browser }} · {{ s.os }}</p>
                  <p class="guv-session-meta">
                    @if (s.ip) { <span>IP: {{ s.ip }}</span> · }
                    <span>Başladı: {{ s.createdAt | date:'dd.MM.yyyy HH:mm' }}</span>
                  </p>
                </div>
                <div class="guv-session-actions">
                  @if (s.isCurrent) {
                    <span class="chip-status is-green shrink-0">Bu cihaz</span>
                  } @else {
                    <button type="button" (click)="revokeOne(s)" [disabled]="revokingOne() === s.id"
                            class="btn btn-sm btn-ghost">
                      {{ revokingOne() === s.id ? 'Çıkış yapılıyor…' : 'Oturumu kapat' }}
                    </button>
                  }
                </div>
              </li>
            }
          </ul>
        }

        <div class="guv-actions">
          <button type="button" (click)="revokeOthers()" [disabled]="revoking() || sessions().length <= 1" class="btn btn-ghost">
            {{ revoking() ? 'Sonlandırılıyor…' : 'Diğer tüm cihazlardan çıkış yap' }}
          </button>
        </div>
      </section>

      <!-- 2FA — TOTP -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head ayarlar-card-head-row">
          <div>
            <h3 class="ayarlar-card-title">Authenticator app (TOTP)</h3>
            <p class="ayarlar-card-desc">Google Authenticator, Authy, 1Password gibi uygulamalarla 6 haneli kod.</p>
          </div>
          @if (status(); as s) {
            <label class="switch">
              <input type="checkbox" [checked]="s.totpEnabled"
                     (change)="onTotpToggle($any($event.target).checked)" />
              <span class="switch-track"><span class="switch-thumb"></span></span>
            </label>
          }
        </header>

        @if (totpSetup(); as setup) {
          <div class="tfa-setup">
            <p class="text-xs text-ink-secondary">Authenticator uygulamanla aşağıdaki QR'ı tara veya gizli anahtarı manuel ekle, sonra uygulamadaki 6 haneli kodu gir.</p>
            <img [src]="setup.qrPngDataUri" alt="TOTP QR" class="tfa-qr" />
            <code class="tfa-secret">{{ setup.secret }}</code>
            <form [formGroup]="totpForm" (ngSubmit)="confirmTotp()" class="tfa-confirm">
              <input type="text" inputmode="numeric" maxlength="6"
                     formControlName="code" class="field-input" placeholder="6 haneli kod" />
              <button type="submit" [disabled]="totpForm.invalid || totpEnabling()" class="btn btn-primary">
                {{ totpEnabling() ? 'Etkinleştiriliyor…' : 'Doğrula ve aktif et' }}
              </button>
              <button type="button" (click)="cancelTotpSetup()" class="btn btn-ghost">Vazgeç</button>
            </form>
          </div>
        }
      </section>

      <!-- 2FA — Email -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head ayarlar-card-head-row">
          <div>
            <h3 class="ayarlar-card-title">E-posta kodu</h3>
            <p class="ayarlar-card-desc">Her giriş denemesinde hesabına bağlı e-postaya 6 haneli kod gönderilir.</p>
          </div>
          @if (status(); as s) {
            <label class="switch">
              <input type="checkbox" [checked]="s.emailOtpEnabled"
                     (change)="onEmailToggle($any($event.target).checked)" />
              <span class="switch-track"><span class="switch-thumb"></span></span>
            </label>
          }
        </header>
      </section>

      <!-- 2FA — WebAuthn -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head ayarlar-card-head-row">
          <div>
            <h3 class="ayarlar-card-title">Cihaz / Passkey (WebAuthn)</h3>
            <p class="ayarlar-card-desc">Touch ID, Windows Hello veya güvenlik anahtarı ile şifresiz doğrulama.</p>
          </div>
          <button type="button" (click)="addPasskey()" [disabled]="passkeyAdding()" class="btn btn-ghost">
            {{ passkeyAdding() ? 'Bekleniyor…' : '+ Cihaz ekle' }}
          </button>
        </header>

        @if (webAuthnList().length > 0) {
          <ul class="tfa-cred-list">
            @for (c of webAuthnList(); track c.id) {
              <li class="tfa-cred-row">
                <div>
                  <p class="tfa-cred-name">{{ c.nickname || 'Cihaz' }}</p>
                  <p class="tfa-cred-meta">Eklendi: {{ c.createdAt | date:'dd.MM.yyyy HH:mm' }}</p>
                </div>
                <button type="button" (click)="removePasskey(c.id)" class="btn btn-ghost btn-sm">Sil</button>
              </li>
            }
          </ul>
        } @else {
          <p class="text-xs text-ink-muted italic">Henüz kayıtlı cihaz yok.</p>
        }
      </section>

      <!-- Recovery codes -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head ayarlar-card-head-row">
          <div>
            <h3 class="ayarlar-card-title">Recovery kodları</h3>
            <p class="ayarlar-card-desc">2FA cihazını kaybedersen hesabına geri girebilmek için tek-kullanımlık 10 kod.</p>
          </div>
          @if (status(); as s) {
            <span class="guv-tag">{{ s.recoveryCodesRemaining }} kod kaldı</span>
          }
        </header>
        @if (recoveryCodes().length > 0) {
          <div class="tfa-codes">
            @for (code of recoveryCodes(); track code) {
              <code>{{ code }}</code>
            }
            <p class="text-[11px] text-coral mt-2">Bu kodları güvenli bir yere kaydet — sayfayı yenileyince bir daha gösterilemez.</p>
          </div>
        }
        <div>
          <button type="button" (click)="regenerateCodes()" [disabled]="regenerating()" class="btn btn-ghost btn-sm">
            {{ regenerating() ? 'Oluşturuluyor…' : 'Yeni kodlar oluştur' }}
          </button>
        </div>
      </section>
    </article>
  `,
  styles: [`
    .ayarlar-page { display: flex; flex-direction: column; gap: 18px; }
    .ayarlar-page-head { display: flex; flex-direction: column; gap: 4px; }
    .ayarlar-page-title {
      font-family: Inter, system-ui, sans-serif;
      font-size: 24px; font-weight: 700; letter-spacing: -0.02em;
      color: var(--color-ink); line-height: 1.1;
    }
    .ayarlar-page-desc {
      font-size: 13px; color: var(--color-ink-secondary); max-width: 56ch;
    }
    .ayarlar-card {
      padding: 18px 20px;
      border-radius: 14px;
      border: 1px solid rgba(0, 0, 0, 0.06);
      background: var(--color-surface);
      display: flex; flex-direction: column; gap: 14px;
    }
    :host-context([data-theme="dark"]) .ayarlar-card { border-color: var(--color-border); }
    .ayarlar-card-head { display: flex; flex-direction: column; gap: 2px; }
    .ayarlar-card-head-row {
      flex-direction: row; align-items: center; justify-content: space-between; gap: 18px;
    }
    .ayarlar-card-title {
      font-family: Inter, system-ui, sans-serif;
      font-size: 14px; font-weight: 700; letter-spacing: -0.01em;
      color: var(--color-ink);
    }
    .ayarlar-card-desc {
      font-size: 12px; color: var(--color-ink-secondary); line-height: 1.45;
      max-width: 64ch;
    }
    .guv-form { display: flex; flex-direction: column; gap: 14px; max-width: 420px; }
    .guv-row { display: flex; flex-direction: column; gap: 6px; }
    .guv-label {
      font-family: Inter, system-ui, sans-serif;
      font-size: 12px; font-weight: 600; letter-spacing: 0.04em;
      color: var(--color-ink-secondary); text-transform: uppercase;
    }
    .guv-error { font-size: 11.5px; color: var(--color-coral, #e84a28); }
    .guv-actions { display: flex; gap: 8px; padding-top: 4px; }
    .guv-session { display: flex; flex-direction: column; gap: 12px; }
    .guv-session-list { display: flex; flex-direction: column; gap: 8px; }
    .guv-session-row {
      display: flex; align-items: center; justify-content: space-between; gap: 14px;
      padding: 12px 14px;
      border: 1px solid rgba(0, 0, 0, 0.06);
      border-radius: 10px;
      background: var(--color-surface-elevated);
    }
    :host-context([data-theme="dark"]) .guv-session-row {
      border-color: var(--color-border);
      background: rgba(255, 255, 255, 0.03);
    }
    .guv-session-info { min-width: 0; }
    .guv-session-actions { flex-shrink: 0; }
    .guv-session-name {
      font-family: Inter, system-ui, sans-serif;
      font-size: 13px; font-weight: 600;
      color: var(--color-ink);
    }
    .guv-session-meta {
      font-size: 11px; color: var(--color-ink-muted);
      display: flex; flex-wrap: wrap; gap: 6px;
    }
    .guv-tag {
      font-family: Inter, system-ui, sans-serif;
      font-size: 10.5px; font-weight: 700; letter-spacing: 0.14em;
      padding: 4px 8px; border-radius: 6px;
      background: var(--color-surface-elevated);
      color: var(--color-ink-secondary);
      flex-shrink: 0;
    }
    .switch { position: relative; display: inline-block; cursor: pointer; flex-shrink: 0; }
    .switch input { position: absolute; opacity: 0; width: 0; height: 0; }
    .switch-track {
      display: block; width: 40px; height: 22px;
      border-radius: 999px;
      background: var(--color-surface-elevated);
      border: 1px solid rgba(0, 0, 0, 0.10);
      position: relative;
      transition: background 160ms, border-color 160ms;
    }
    .switch-thumb {
      position: absolute; top: 2px; left: 2px;
      width: 16px; height: 16px;
      border-radius: 50%; background: var(--color-ink);
      transition: transform 200ms cubic-bezier(0.16, 1, 0.3, 1);
    }
    .switch input:checked + .switch-track {
      background: rgba(var(--color-accent-rgb), 0.30);
      border-color: rgba(var(--color-accent-rgb), 0.50);
    }
    .switch input:checked + .switch-track .switch-thumb {
      transform: translateX(18px); background: var(--color-accent, #14b8a6);
    }
    .tfa-setup {
      display: flex; flex-direction: column; align-items: flex-start; gap: 12px;
      padding-top: 8px;
    }
    .tfa-qr {
      width: 180px; height: 180px;
      border: 1px solid rgba(0, 0, 0, 0.06);
      border-radius: 8px;
      background: #fff;
    }
    .tfa-secret {
      font-family: 'JetBrains Mono', monospace; font-size: 12px;
      padding: 6px 10px;
      background: var(--color-surface-elevated);
      border-radius: 6px;
      letter-spacing: 0.08em;
    }
    .tfa-confirm { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .tfa-cred-list { display: flex; flex-direction: column; gap: 6px; }
    .tfa-cred-row {
      display: flex; justify-content: space-between; align-items: center; gap: 12px;
      padding: 10px 12px;
      border: 1px solid rgba(0, 0, 0, 0.06);
      border-radius: 10px;
      background: var(--color-surface-elevated);
    }
    .tfa-cred-name {
      font-family: Inter, system-ui, sans-serif; font-weight: 600; font-size: 13px;
      color: var(--color-ink);
    }
    .tfa-cred-meta { font-size: 11px; color: var(--color-ink-muted); }
    .tfa-codes {
      display: grid; grid-template-columns: repeat(2, 1fr); gap: 6px;
      padding: 12px;
      background: var(--color-surface-elevated);
      border-radius: 10px;
    }
    .tfa-codes code {
      font-family: 'JetBrains Mono', monospace; font-size: 13px;
      letter-spacing: 0.08em; text-align: center;
      padding: 6px 8px;
      background: var(--color-surface);
      border-radius: 6px;
    }
  `],
})
export class GuvenlikComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly form = this.fb.nonNullable.group({
    currentPassword: ['', [Validators.required]],
    newPassword:     ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', [Validators.required]],
  });

  readonly totpForm = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.minLength(6), Validators.maxLength(6)]],
  });

  readonly saving = signal(false);
  readonly revoking = signal(false);
  readonly serverError = signal<string | null>(null);

  // Sessions state
  readonly sessions = signal<ActiveSession[]>([]);
  readonly sessionsLoading = signal(false);
  readonly revokingOne = signal<string | null>(null);

  // 2FA state
  readonly status = signal<TwoFactorStatus | null>(null);
  readonly totpSetup = signal<TotpSetupResponse | null>(null);
  readonly totpEnabling = signal(false);
  readonly webAuthnList = signal<WebAuthnCredentialDto[]>([]);
  readonly passkeyAdding = signal(false);
  readonly recoveryCodes = signal<string[]>([]);
  readonly regenerating = signal(false);

  ngOnInit(): void {
    this.refreshStatus();
    this.refreshWebAuthnList();
    this.refreshSessions();
  }

  refreshSessions(): void {
    this.sessionsLoading.set(true);
    this.auth.listSessions().subscribe({
      next: (list) => { this.sessions.set(list); this.sessionsLoading.set(false); },
      error: () => { this.sessionsLoading.set(false); this.toast.error('Oturumlar yüklenemedi.'); },
    });
  }

  async revokeOne(s: ActiveSession): Promise<void> {
    if (this.revokingOne()) return;
    const ok = await this.confirm.ask({
      title: 'Oturumu kapat',
      message: `${s.browser} · ${s.os} cihazındaki oturum sonlandırılacak. Devam edilsin mi?`,
      confirmLabel: 'Kapat', cancelLabel: 'Vazgeç',
    });
    if (!ok) return;
    this.revokingOne.set(s.id);
    this.auth.revokeSession(s.id).subscribe({
      next: () => {
        this.revokingOne.set(null);
        this.sessions.update((list) => list.filter((x) => x.id !== s.id));
        this.toast.success('Oturum kapatıldı.');
      },
      error: (err: HttpErrorResponse) => {
        this.revokingOne.set(null);
        this.toast.error(err.error?.message ?? 'Oturum kapatılamadı.');
      },
    });
  }

  private refreshStatus(): void {
    this.auth.twoFactorStatus().subscribe({
      next: (s) => this.status.set(s),
      error: () => {},
    });
  }
  private refreshWebAuthnList(): void {
    this.auth.webAuthnList().subscribe({
      next: (list) => this.webAuthnList.set(list),
      error: () => {},
    });
  }

  // ─── TOTP ────────────────────────────────────────────────────────────────
  onTotpToggle(checked: boolean): void {
    if (checked) {
      // Enabling means starting the setup ceremony — the actual enable happens after code confirm.
      if (this.status()?.totpEnabled) return;
      this.auth.totpSetup().subscribe({
        next: (s) => { this.totpSetup.set(s); this.totpForm.reset({ code: '' }); },
        error: () => this.toast.error('TOTP setup başlatılamadı.'),
      });
    } else {
      // Disable directly.
      this.auth.totpDisable().subscribe({
        next: () => { this.totpSetup.set(null); this.refreshStatus(); this.toast.info('TOTP kapatıldı.'); },
        error: () => this.toast.error('TOTP kapatılamadı.'),
      });
    }
  }

  cancelTotpSetup(): void { this.totpSetup.set(null); }

  confirmTotp(): void {
    if (this.totpForm.invalid || this.totpEnabling()) return;
    this.totpEnabling.set(true);
    this.auth.totpEnable(this.totpForm.controls.code.value).subscribe({
      next: (res) => {
        this.totpEnabling.set(false);
        this.totpSetup.set(null);
        if (res.codes && res.codes.length > 0) this.recoveryCodes.set(res.codes);
        this.refreshStatus();
        this.toast.success('TOTP aktif edildi.');
      },
      error: (err: HttpErrorResponse) => {
        this.totpEnabling.set(false);
        this.toast.error(err.error?.message ?? 'Kod hatalı.');
      },
    });
  }

  // ─── Email OTP ───────────────────────────────────────────────────────────
  onEmailToggle(checked: boolean): void {
    const op = checked ? this.auth.emailOtpEnable() : this.auth.emailOtpDisable();
    op.subscribe({
      next: () => {
        this.refreshStatus();
        this.toast.info(checked ? 'E-posta 2FA açıldı.' : 'E-posta 2FA kapatıldı.');
      },
      error: () => this.toast.error('İşlem başarısız.'),
    });
  }

  // ─── WebAuthn ────────────────────────────────────────────────────────────
  async addPasskey(): Promise<void> {
    if (this.passkeyAdding()) return;
    if (!('credentials' in navigator)) {
      this.toast.error('Tarayıcın WebAuthn desteklemiyor.');
      return;
    }
    this.passkeyAdding.set(true);
    try {
      const optsJson = await new Promise<any>((resolve, reject) =>
        this.auth.webAuthnRegisterOptions().subscribe({ next: resolve, error: reject }));
      const opts = decodeCreationOptions(optsJson);
      const cred = (await navigator.credentials.create({ publicKey: opts })) as PublicKeyCredential | null;
      if (!cred) throw new Error('İptal edildi.');
      const payload = encodeAttestationResponse(cred);
      const nickname = window.prompt('Bu cihaza ad ver (örn. "iPhone", "Office laptop"):', '') || undefined;
      this.auth.webAuthnRegisterComplete(payload, nickname).subscribe({
        next: () => {
          this.passkeyAdding.set(false);
          this.refreshStatus();
          this.refreshWebAuthnList();
          this.toast.success('Cihaz eklendi.');
        },
        error: (err: HttpErrorResponse) => {
          this.passkeyAdding.set(false);
          this.toast.error(err.error?.message ?? 'Cihaz eklenemedi.');
        },
      });
    } catch (e: any) {
      this.passkeyAdding.set(false);
      this.toast.error(e?.message ?? 'WebAuthn başarısız.');
    }
  }

  async removePasskey(id: string): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Cihazı sil',
      message: 'Bu cihaz 2FA için kullanılamaz hale gelecek. Devam edilsin mi?',
      confirmLabel: 'Sil', cancelLabel: 'Vazgeç', danger: true,
    });
    if (!ok) return;
    this.auth.webAuthnDelete(id).subscribe({
      next: () => { this.refreshStatus(); this.refreshWebAuthnList(); this.toast.success('Cihaz silindi.'); },
      error: () => this.toast.error('Silinemedi.'),
    });
  }

  // ─── Recovery codes ──────────────────────────────────────────────────────
  regenerateCodes(): void {
    if (this.regenerating()) return;
    this.regenerating.set(true);
    this.auth.regenerateRecoveryCodes().subscribe({
      next: (res) => {
        this.regenerating.set(false);
        this.recoveryCodes.set(res.codes);
        this.refreshStatus();
        this.toast.info('Yeni recovery kodları üretildi.');
      },
      error: () => { this.regenerating.set(false); this.toast.error('Üretilemedi.'); },
    });
  }

  passwordMismatch(): boolean {
    const v = this.form.getRawValue();
    return !!v.newPassword && !!v.confirmPassword && v.newPassword !== v.confirmPassword;
  }

  submit(): void {
    if (this.saving()) return;
    if (this.form.invalid || this.passwordMismatch()) {
      this.form.markAllAsTouched();
      return;
    }
    this.serverError.set(null);
    this.saving.set(true);
    const { currentPassword, newPassword } = this.form.getRawValue();
    this.auth.changePassword({ currentPassword, newPassword }).subscribe({
      next: () => {
        this.saving.set(false);
        this.form.reset();
        this.toast.success('Parolan güncellendi. Diğer cihazlardan çıkış yapıldı.');
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.serverError.set(err.error?.message ?? 'Parola değiştirilemedi.');
      },
    });
  }

  async revokeOthers(): Promise<void> {
    if (this.revoking()) return;
    const ok = await this.confirm.ask({
      title: 'Diğer cihazlardan çıkış yap',
      message: 'Bu cihaz dışındaki tüm aktif oturumlar sonlandırılacak. Devam edilsin mi?',
      confirmLabel: 'Sonlandır',
      cancelLabel: 'Vazgeç',
    });
    if (!ok) return;

    this.revoking.set(true);
    this.auth.revokeOtherSessions().subscribe({
      next: ({ revoked }) => {
        this.revoking.set(false);
        if (revoked === 0) {
          this.toast.info('Sonlandırılacak başka aktif oturum bulunamadı.');
        } else {
          this.toast.success(`${revoked} cihazdan çıkış yapıldı.`);
        }
        this.refreshSessions();
      },
      error: () => {
        this.revoking.set(false);
        this.toast.error('İşlem başarısız oldu.');
      },
    });
  }
}
