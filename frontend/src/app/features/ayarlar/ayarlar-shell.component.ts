import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { UserRole } from '../../core/auth/auth.models';

interface AyarKategori {
  path: string;
  label: string;
  description: string;
  iconImg: string;
  roles?: UserRole[];
}

@Component({
  selector: 'app-ayarlar-shell',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ayarlar-shell">
      <aside class="ayarlar-side">
        <div class="ayarlar-side-head">
          <p class="section-label">Hesap</p>
          <h1 class="ayarlar-side-title">Ayarlar</h1>
        </div>
        <nav class="ayarlar-side-nav">
          @for (k of visibleKategoriler(); track k.path) {
            <a
              [routerLink]="k.path"
              routerLinkActive="active"
              [routerLinkActiveOptions]="{ exact: false }"
              class="ayarlar-side-item"
            >
              <span class="ayarlar-side-icon" aria-hidden="true">
                <img [src]="k.iconImg" alt="" />
              </span>
              <span class="ayarlar-side-text">
                <span class="ayarlar-side-label">{{ k.label }}</span>
                <span class="ayarlar-side-desc">{{ k.description }}</span>
              </span>
            </a>
          }
        </nav>
      </aside>
      <section class="ayarlar-content">
        <router-outlet />
      </section>
    </div>
  `,
  styles: [`
    :host {
      display: block;
    }
    .ayarlar-shell {
      display: grid;
      grid-template-columns: 260px 1fr;
      gap: 24px;
      align-items: start;
      min-width: 0;
    }
    .ayarlar-side {
      position: sticky;
      top: 24px;
      align-self: start;
      max-height: calc(100vh - 48px);
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 14px;
      padding: 18px 16px;
      border: 1px solid rgba(0, 0, 0, 0.06);
      border-radius: 14px;
      background: var(--color-surface);
    }
    :host-context([data-theme="dark"]) .ayarlar-side {
      border-color: var(--color-border);
    }
    .ayarlar-side-head { padding: 0 4px; }
    .ayarlar-side-title {
      font-family: Inter, system-ui, sans-serif;
      font-size: 22px;
      font-weight: 700;
      letter-spacing: -0.02em;
      color: var(--color-ink);
      line-height: 1.1;
    }
    .ayarlar-side-nav { display: flex; flex-direction: column; gap: 4px; }
    .ayarlar-side-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 12px;
      border-radius: 10px;
      border: 1px solid transparent;
      text-decoration: none;
      color: var(--color-ink);
      transition: background 140ms, border-color 140ms;
    }
    .ayarlar-side-item:hover {
      background: var(--color-surface-elevated);
      border-color: rgba(0, 0, 0, 0.06);
    }
    .ayarlar-side-item.active {
      background: var(--color-accent-soft, rgba(var(--color-accent-rgb), 0.10));
      border-color: rgba(var(--color-accent-rgb), 0.28);
    }
    :host-context([data-theme="dark"]) .ayarlar-side-item { color: var(--color-ink-secondary); }
    :host-context([data-theme="dark"]) .ayarlar-side-item:hover {
      background: rgba(255, 255, 255, 0.04);
      border-color: var(--color-border-strong);
      color: var(--color-ink);
    }
    :host-context([data-theme="dark"]) .ayarlar-side-item.active {
      background: rgba(var(--color-accent-rgb), 0.16);
      border-color: rgba(var(--color-accent-rgb), 0.36);
      color: var(--color-ink);
    }
    .ayarlar-side-icon {
      display: inline-flex; align-items: center; justify-content: center;
      width: 36px; height: 36px;
      flex-shrink: 0;
    }
    .ayarlar-side-icon img {
      width: 30px;
      height: 30px;
      object-fit: contain;
      display: block;
    }
    .ayarlar-side-text {
      display: flex; flex-direction: column; min-width: 0;
    }
    .ayarlar-side-label {
      font-family: Inter, system-ui, sans-serif;
      font-size: 13px;
      font-weight: 600;
      letter-spacing: -0.01em;
      color: var(--color-ink);
    }
    .ayarlar-side-desc {
      font-size: 11px;
      color: var(--color-ink-muted);
    }
    .ayarlar-content {
      min-width: 0;
    }
    @media (max-width: 768px) {
      .ayarlar-shell { grid-template-columns: 1fr; gap: 16px; }
      .ayarlar-side {
        position: static;
        max-height: none;
        padding: 14px 12px;
      }
      /* On phones the side nav becomes a horizontal scroller of compact chips
         so it doesn't push the actual settings content below the fold. */
      .ayarlar-side-nav {
        flex-direction: row;
        overflow-x: auto;
        gap: 8px;
        scrollbar-width: none;
        -ms-overflow-style: none;
      }
      .ayarlar-side-nav::-webkit-scrollbar { display: none; }
      .ayarlar-side-item {
        flex: 0 0 auto;
        flex-direction: column;
        align-items: center;
        gap: 4px;
        padding: 8px 10px;
        min-width: 80px;
        text-align: center;
      }
      .ayarlar-side-icon { width: 28px; height: 28px; }
      .ayarlar-side-icon img { width: 22px; height: 22px; }
      .ayarlar-side-desc { display: none; }
      .ayarlar-side-label { font-size: 11.5px; }
    }
  `],
})
export class AyarlarShellComponent {
  private readonly auth = inject(AuthService);
  private readonly kategoriler: AyarKategori[] = [
    { path: 'profil',     label: 'Profilim',           description: 'Ad, e-posta, avatar',     iconImg: '/assets/images/avatar-design.png' },
    { path: 'genel',      label: 'Genel ayarlar',      description: 'Dil, bölge, görünüm',     iconImg: '/assets/images/gear.png' },
    { path: 'guvenlik',   label: 'Gizlilik & güvenlik', description: 'Şifre, oturumlar, 2FA',  iconImg: '/assets/images/shield.png' },
    { path: 'bildirimler', label: 'Bildirimler',       description: 'E-posta & in-app',        iconImg: '/assets/images/notification.png' },
  ];

  protected readonly visibleKategoriler = computed<AyarKategori[]>(() => {
    const rol = this.auth.currentUser()?.rol;
    if (!rol) return [];
    return this.kategoriler.filter((k) => !k.roles || k.roles.includes(rol));
  });
}
