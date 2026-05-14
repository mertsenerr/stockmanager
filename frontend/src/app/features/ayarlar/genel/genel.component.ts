import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ThemeService, ThemeMode } from '../../../core/theme/theme.service';
import { PreferencesService, DateFormat, Language } from '../../../core/preferences/preferences.service';

@Component({
  selector: 'app-ayarlar-genel',
  standalone: true,
  imports: [],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="ayarlar-page">
      <header class="ayarlar-page-head">
        <h2 class="ayarlar-page-title">Genel ayarlar</h2>
        <p class="ayarlar-page-desc">Görünüm, dil ve format tercihleriniz. Tüm değişiklikler bu cihaza özeldir ve anında uygulanır.</p>
      </header>

      <!-- Tema -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head">
          <h3 class="ayarlar-card-title">Tema</h3>
          <p class="ayarlar-card-desc">Açık ya da koyu görünüm.</p>
        </header>
        <div class="seg" role="radiogroup" aria-label="Tema seçimi">
          @for (opt of themeOptions; track opt.value) {
            <button
              type="button"
              class="seg-item"
              [class.is-active]="theme() === opt.value"
              role="radio"
              [attr.aria-checked]="theme() === opt.value"
              (click)="setTheme(opt.value)"
            >
              <span class="seg-icon" aria-hidden="true">{{ opt.icon }}</span>
              <span class="seg-label">{{ opt.label }}</span>
            </button>
          }
        </div>
      </section>

      <!-- Dil -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head">
          <h3 class="ayarlar-card-title">Dil</h3>
          <p class="ayarlar-card-desc">Arayüz dili. Yeni diller yakında eklenecek.</p>
        </header>
        <div class="ayarlar-row">
          <select
            class="field-input"
            [value]="language()"
            (change)="setLanguage($any($event.target).value)"
            style="max-width: 280px"
          >
            <option value="tr">Türkçe</option>
            <option value="en" disabled>English (yakında)</option>
          </select>
        </div>
      </section>

      <!-- Tarih formatı -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head">
          <h3 class="ayarlar-card-title">Tarih formatı</h3>
          <p class="ayarlar-card-desc">
            Önizleme: <strong>{{ datePreview() }}</strong>
          </p>
        </header>
        <div class="seg" role="radiogroup" aria-label="Tarih formatı">
          @for (opt of dateOptions; track opt.value) {
            <button
              type="button"
              class="seg-item"
              [class.is-active]="dateFormat() === opt.value"
              role="radio"
              [attr.aria-checked]="dateFormat() === opt.value"
              (click)="setDateFormat(opt.value)"
            >
              <span class="seg-label">{{ opt.label }}</span>
              <span class="seg-sub">{{ opt.preview }}</span>
            </button>
          }
        </div>
      </section>

      <!-- Hareket azalt -->
      <section class="ayarlar-card">
        <header class="ayarlar-card-head ayarlar-card-head-row">
          <div>
            <h3 class="ayarlar-card-title">Hareketleri azalt</h3>
            <p class="ayarlar-card-desc">Geçiş animasyonlarını kapatır. Hareket hassasiyetin varsa veya cihazın yavaşsa açabilirsin.</p>
          </div>
          <label class="switch">
            <input type="checkbox" [checked]="reduceMotion()" (change)="setReduceMotion($any($event.target).checked)" />
            <span class="switch-track"><span class="switch-thumb"></span></span>
          </label>
        </header>
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
    :host-context([data-theme="dark"]) .ayarlar-card {
      border-color: var(--color-border);
    }
    .ayarlar-card-head { display: flex; flex-direction: column; gap: 2px; }
    .ayarlar-card-head-row {
      flex-direction: row;
      align-items: center;
      justify-content: space-between;
      gap: 18px;
    }
    .ayarlar-card-title {
      font-family: Inter, system-ui, sans-serif;
      font-size: 14px; font-weight: 700; letter-spacing: -0.01em;
      color: var(--color-ink);
    }
    .ayarlar-card-desc {
      font-size: 12px; color: var(--color-ink-secondary); line-height: 1.45;
    }
    .ayarlar-row { display: flex; flex-wrap: wrap; gap: 8px; }

    /* Segmented control */
    .seg {
      display: inline-flex; flex-wrap: wrap; gap: 6px;
      padding: 4px;
      border: 1px solid rgba(0, 0, 0, 0.06);
      border-radius: 12px;
      background: var(--color-surface-elevated);
    }
    :host-context([data-theme="dark"]) .seg {
      background: rgba(255, 255, 255, 0.03);
      border-color: var(--color-border);
    }
    .seg-item {
      display: inline-flex; flex-direction: column; align-items: center; gap: 2px;
      min-width: 96px;
      padding: 8px 14px;
      border: 1px solid transparent;
      border-radius: 8px;
      background: transparent;
      cursor: pointer;
      color: var(--color-ink-secondary);
      transition: background 140ms, border-color 140ms, color 140ms;
    }
    .seg-item:hover { color: var(--color-ink); }
    .seg-item.is-active {
      background: var(--color-surface);
      border-color: rgba(var(--color-accent-rgb), 0.30);
      color: var(--color-ink);
      box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
    }
    :host-context([data-theme="dark"]) .seg-item.is-active {
      background: rgba(var(--color-accent-rgb), 0.16);
      border-color: rgba(var(--color-accent-rgb), 0.40);
    }
    .seg-icon { font-size: 14px; }
    .seg-label {
      font-family: Inter, system-ui, sans-serif;
      font-size: 12.5px; font-weight: 600;
    }
    .seg-sub {
      font-family: 'JetBrains Mono', monospace;
      font-size: 10.5px; color: var(--color-ink-muted);
    }

    /* Toggle switch */
    .switch { position: relative; display: inline-block; cursor: pointer; flex-shrink: 0; }
    .switch input { position: absolute; opacity: 0; width: 0; height: 0; }
    .switch-track {
      display: block;
      width: 40px; height: 22px;
      border-radius: 999px;
      background: var(--color-surface-elevated);
      border: 1px solid rgba(0, 0, 0, 0.10);
      position: relative;
      transition: background 160ms, border-color 160ms;
    }
    .switch-thumb {
      position: absolute;
      top: 2px; left: 2px;
      width: 16px; height: 16px;
      border-radius: 50%;
      background: var(--color-ink);
      transition: transform 200ms cubic-bezier(0.16, 1, 0.3, 1), background 160ms;
    }
    .switch input:checked + .switch-track {
      background: rgba(var(--color-accent-rgb), 0.30);
      border-color: rgba(var(--color-accent-rgb), 0.50);
    }
    .switch input:checked + .switch-track .switch-thumb {
      transform: translateX(18px);
      background: var(--color-accent, #14b8a6);
    }
    .switch input:focus-visible + .switch-track {
      outline: 2px solid rgba(var(--color-accent-rgb), 0.45);
      outline-offset: 2px;
    }
    :host-context([data-theme="dark"]) .switch-track {
      border-color: var(--color-border-strong);
    }
    :host-context([data-theme="dark"]) .switch-thumb { background: var(--color-ink-secondary); }
  `],
})
export class GenelComponent {
  private readonly themeSvc = inject(ThemeService);
  private readonly prefs = inject(PreferencesService);

  readonly theme = this.themeSvc.theme;
  readonly language = computed(() => this.prefs.prefs().language);
  readonly dateFormat = computed(() => this.prefs.prefs().dateFormat);
  readonly reduceMotion = computed(() => this.prefs.prefs().reduceMotion);

  readonly themeOptions: { value: ThemeMode; label: string; icon: string }[] = [
    { value: 'light', label: 'Açık', icon: '☼' },
    { value: 'dark',  label: 'Koyu', icon: '☾' },
  ];

  readonly dateOptions: { value: DateFormat; label: string; preview: string }[] = [
    { value: 'tr',  label: 'TR',  preview: '14.05.2026' },
    { value: 'iso', label: 'ISO', preview: '2026-05-14' },
    { value: 'us',  label: 'US',  preview: '05/14/2026' },
  ];

  readonly datePreview = computed(() => this.prefs.formatDate(new Date()));

  setTheme(t: ThemeMode): void { this.themeSvc.set(t); }
  setLanguage(l: Language): void { this.prefs.setLanguage(l); }
  setDateFormat(f: DateFormat): void { this.prefs.setDateFormat(f); }
  setReduceMotion(v: boolean): void { this.prefs.setReduceMotion(v); }
}
