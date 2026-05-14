import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { PreferencesService, NotificationKey } from '../../../core/preferences/preferences.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';

interface NotifSwitch {
  key: NotificationKey;
  label: string;
  desc: string;
}

@Component({
  selector: 'app-ayarlar-bildirimler',
  standalone: true,
  imports: [],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="ayarlar-page">
      <header class="ayarlar-page-head">
        <div class="ayarlar-page-head-row">
          <div>
            <h2 class="ayarlar-page-title">Bildirimler</h2>
            <p class="ayarlar-page-desc">Hangi olaylarda haberdar olmak istersin? Tercihler bu cihaza özeldir; sunucu tarafında bildirim gönderimi yakında bu seçimlere göre filtrelenecek.</p>
          </div>
          <button type="button" (click)="reset()" class="btn btn-ghost btn-sm">Varsayılana döndür</button>
        </div>
      </header>

      <!-- DND -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head ayarlar-card-head-row">
          <div>
            <h3 class="ayarlar-card-title">Rahatsız etme</h3>
            <p class="ayarlar-card-desc">Açıkken bütün bildirimler susturulur. Önemli olaylar zaman çizelgesinde görünür ama ses/popup olmaz.</p>
          </div>
          <label class="switch">
            <input type="checkbox" [checked]="dnd()" (change)="set('dndEnabled', $any($event.target).checked)" />
            <span class="switch-track"><span class="switch-thumb"></span></span>
          </label>
        </header>
      </section>

      <!-- Email bildirimleri -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head">
          <h3 class="ayarlar-card-title">E-posta bildirimleri</h3>
          <p class="ayarlar-card-desc">Hesabına bağlı e-posta adresine gidecek özet ve uyarılar.</p>
        </header>
        <div class="bld-list">
          @for (s of emailSwitches; track s.key) {
            <div class="bld-row">
              <div>
                <p class="bld-row-label">{{ s.label }}</p>
                <p class="bld-row-desc">{{ s.desc }}</p>
              </div>
              <label class="switch">
                <input type="checkbox"
                       [disabled]="dnd()"
                       [checked]="value(s.key)"
                       (change)="set(s.key, $any($event.target).checked)" />
                <span class="switch-track"><span class="switch-thumb"></span></span>
              </label>
            </div>
          }
        </div>
      </section>

      <!-- In-app bildirimleri -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head">
          <h3 class="ayarlar-card-title">Uygulama içi bildirimler</h3>
          <p class="ayarlar-card-desc">Uygulama açıkken sağ üstte beliren toast'lar ve sayım sayfasındaki canlı uyarılar.</p>
        </header>
        <div class="bld-list">
          @for (s of inappSwitches; track s.key) {
            <div class="bld-row">
              <div>
                <p class="bld-row-label">{{ s.label }}</p>
                <p class="bld-row-desc">{{ s.desc }}</p>
              </div>
              <label class="switch">
                <input type="checkbox"
                       [disabled]="dnd()"
                       [checked]="value(s.key)"
                       (change)="set(s.key, $any($event.target).checked)" />
                <span class="switch-track"><span class="switch-thumb"></span></span>
              </label>
            </div>
          }
        </div>
      </section>

      <!-- Ses -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head ayarlar-card-head-row">
          <div>
            <h3 class="ayarlar-card-title">Bildirim sesi</h3>
            <p class="ayarlar-card-desc">Yeni bir bildirim geldiğinde kısa bir uyarı sesi çal.</p>
          </div>
          <label class="switch">
            <input type="checkbox"
                   [disabled]="dnd()"
                   [checked]="value('soundEnabled')"
                   (change)="set('soundEnabled', $any($event.target).checked)" />
            <span class="switch-track"><span class="switch-thumb"></span></span>
          </label>
        </header>
      </section>
    </article>
  `,
  styles: [`
    .ayarlar-page { display: flex; flex-direction: column; gap: 18px; }
    .ayarlar-page-head { display: flex; flex-direction: column; gap: 4px; }
    .ayarlar-page-head-row {
      display: flex; align-items: flex-start; justify-content: space-between; gap: 18px;
    }
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

    .bld-list { display: flex; flex-direction: column; gap: 4px; }
    .bld-row {
      display: flex; align-items: center; justify-content: space-between; gap: 18px;
      padding: 10px 4px;
      border-bottom: 1px solid rgba(0, 0, 0, 0.04);
    }
    .bld-row:last-child { border-bottom: none; }
    :host-context([data-theme="dark"]) .bld-row {
      border-bottom-color: rgba(255, 255, 255, 0.04);
    }
    .bld-row-label {
      font-family: Inter, system-ui, sans-serif;
      font-size: 13px; font-weight: 600;
      color: var(--color-ink);
    }
    .bld-row-desc {
      font-size: 11.5px; color: var(--color-ink-muted);
      max-width: 52ch;
    }
  `],
})
export class BildirimlerComponent {
  private readonly prefs = inject(PreferencesService);
  private readonly toast = inject(ToastService);

  readonly notifications = computed(() => this.prefs.prefs().notifications);
  readonly dnd = computed(() => this.notifications().dndEnabled);

  readonly emailSwitches: NotifSwitch[] = [
    { key: 'emailSayimAtamasi', label: 'Sayım atamaları',     desc: 'Sana yeni bir sayım atandığında veya tarih değiştiğinde.' },
    { key: 'emailOnayBekleyen', label: 'Onay bekleyen talepler', desc: 'Sayım başkanıysan, sayman talebi geldiğinde özet e-posta.' },
    { key: 'emailArkadaslik',   label: 'Arkadaşlık istekleri', desc: 'Yeni bir arkadaşlık isteği geldiğinde.' },
  ];

  readonly inappSwitches: NotifSwitch[] = [
    { key: 'inappSayimHareketi', label: 'Sayım hareketleri',  desc: 'Bir sayman katıldı/ayrıldı, satır güncellendi gibi.' },
    { key: 'inappCagri',         label: 'Sesli/görüntülü çağrılar', desc: 'Aktif sayımda biri seni aradığında.' },
    { key: 'inappSistem',        label: 'Sistem duyuruları',  desc: 'Bakım, sürüm yenilemeleri, önemli güncellemeler.' },
  ];

  value(key: NotificationKey): boolean {
    return this.notifications()[key];
  }

  set(key: NotificationKey, value: boolean): void {
    this.prefs.setNotification(key, value);
  }

  reset(): void {
    this.prefs.resetNotifications();
    this.toast.info('Bildirim tercihleri varsayılana döndürüldü.');
  }
}
