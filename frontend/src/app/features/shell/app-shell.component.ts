import { ChangeDetectionStrategy, Component, HostListener, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ROLE_LABELS, UserRole } from '../../core/auth/auth.models';
import { ThemeService } from '../../core/theme/theme.service';
import { ToastHostComponent } from '../../shared/ui/toast/toast-host.component';
import { ToastService } from '../../shared/ui/toast/toast.service';
import { ConfirmHostComponent } from '../../shared/ui/confirm/confirm-host.component';
import { StepUpHostComponent } from '../../shared/ui/step-up/step-up-host.component';
import { IncomingCallService } from '../../core/realtime/incoming-call.service';
import { IncomingCallHostComponent } from '../../core/realtime/incoming-call-host.component';
import { CommandPaletteComponent } from './command-palette/command-palette.component';
import { HoverGifComponent } from './hover-gif.component';

interface SubItem {
  label: string;
  path: string;
  description: string;
  badge?: string;
  iconImg?: string;
  roles?: UserRole[];
}

interface NavGroup {
  id: string;
  label: string;
  icon: string;
  iconImg?: string;
  description: string;
  items: SubItem[];
  roles?: UserRole[];
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, ToastHostComponent, ConfirmHostComponent, StepUpHostComponent, IncomingCallHostComponent, CommandPaletteComponent, HoverGifComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app-shell.component.html',
  styleUrls: ['./app-shell.component.css'],
})
export class AppShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly incomingCalls = inject(IncomingCallService);
  private readonly themeSvc = inject(ThemeService);
  private readonly toast = inject(ToastService);

  readonly theme = this.themeSvc.theme;
  readonly toggleTheme = () => this.themeSvc.toggle();

  readonly user = this.auth.currentUser;
  readonly roleLabel = computed(() => {
    const u = this.user();
    return u ? ROLE_LABELS[u.rol] : '';
  });
  readonly initials = computed(() => {
    const u = this.user();
    if (!u) return '··';
    return u.adSoyad
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((s) => s[0]?.toUpperCase() ?? '')
      .join('') || '··';
  });

  readonly mobileNavOpen = signal(false);
  readonly currentPath = signal<string>(this.router.url);
  readonly paletteOpen = signal(false);
  readonly hoveredGroupId = signal<string | null>(null);
  readonly profileMenuOpen = signal(false);

  isGif(url: string | undefined): boolean {
    return !!url && url.toLowerCase().endsWith('.gif');
  }

  openPalette(): void { this.paletteOpen.set(true); }
  closePalette(): void { this.paletteOpen.set(false); }

  toggleProfileMenu(): void { this.profileMenuOpen.update((v) => !v); }
  closeProfileMenu(): void { this.profileMenuOpen.set(false); }

  /** Placeholder handler — settings pages aren't built yet. */
  notYetImplemented(label: string): void {
    this.closeProfileMenu();
    this.toast.info(`${label} — yakında.`);
  }

  @HostListener('document:keydown', ['$event'])
  onGlobalKeydown(ev: KeyboardEvent): void {
    // Cmd+K (macOS) or Ctrl+K (Win/Linux) opens the command palette
    if ((ev.metaKey || ev.ctrlKey) && (ev.key === 'k' || ev.key === 'K')) {
      ev.preventDefault();
      this.paletteOpen.update((v) => !v);
    }
    if (ev.key === 'Escape' && this.profileMenuOpen()) {
      this.closeProfileMenu();
    }
  }

  private readonly allGroups: NavGroup[] = [
    {
      id: 'home',
      label: 'Anasayfa',
      icon: '◎',
      iconImg: '/assets/images/menu.png',
      description: 'Hızlı bakış',
      items: [
        { label: 'Anasayfa', path: '/', description: 'Hesap & sistem özet', iconImg: '/assets/images/menu%20(2).png' },
      ],
    },
    {
      id: 'compare',
      label: 'Karşılaştırma',
      icon: '◐',
      iconImg: '/assets/images/comparison.png',
      description: 'Sayım & raporlar',
      items: [
        { label: 'Oturumlar', path: '/oturumlar', description: 'Aktif & geçmiş canlı sayımlar', iconImg: '/assets/images/business.png' },
        { label: 'Özel Raporlar', path: '/ozel-raporlar', description: 'Excel/PDF paylaşımlı raporlar', iconImg: '/assets/images/document.png' },
      ],
    },
    {
      id: 'data',
      label: 'Veri',
      icon: '◇',
      iconImg: '/assets/images/data.png',
      description: 'Firmalar & mağazalar',
      roles: ['Sistem', 'SayimBaskani'],
      items: [
        { label: 'Firmalar', path: '/firmalar', description: 'Müşteri firma listesi', iconImg: '/assets/images/empire-state-building.png' },
        { label: 'Mağazalar', path: '/magazalar', description: 'Şube ağı', iconImg: '/assets/images/online-shopping.png' },
      ],
    },
    {
      id: 'people',
      label: 'Hesap',
      icon: '☻',
      iconImg: '/assets/images/friends.png',
      description: 'Kullanıcılar & arkadaşlar',
      items: [
        { label: 'Arkadaşlar', path: '/arkadaslar', description: 'Arkadaş listesi & istekler', iconImg: '/assets/images/add-user.png' },
        { label: 'Kullanıcılar', path: '/kullanicilar', description: 'Sistem kullanıcı yönetimi', roles: ['Sistem'] },
      ],
    },
    {
      id: 'system',
      label: 'Sistem',
      icon: '⚙',
      description: 'Yönetim araçları',
      roles: ['Sistem'],
      items: [
        { label: 'Takvim', path: '/takvim', description: 'Atama takvimi' },
        { label: 'Audit', path: '/audit', description: 'Değişiklik kayıtları' },
      ],
    },
  ];

  readonly groups = computed<NavGroup[]>(() => {
    const role = this.user()?.rol;
    if (!role) return [];
    return this.allGroups
      .filter((g) => !g.roles || g.roles.includes(role))
      .map((g) => ({
        ...g,
        items: g.items.filter((i) => !i.roles || i.roles.includes(role)),
      }))
      .filter((g) => g.items.length > 0);
  });

  /** Hangi grup aktif — URL'den çıkarılır, kullanıcı tıklarsa override edilir. */
  private readonly _activeGroupId = signal<string | null>(null);
  readonly activeGroupId = computed<string>(() => {
    const overridden = this._activeGroupId();
    if (overridden) return overridden;
    const path = this.currentPath();
    const match = this.groups().find((g) => g.items.some((i) => this.matchesPath(i.path, path)));
    return match?.id ?? this.groups()[0]?.id ?? '';
  });

  readonly activeGroup = computed<NavGroup | undefined>(
    () => this.groups().find((g) => g.id === this.activeGroupId()),
  );

  setActiveGroup(id: string): void {
    this._activeGroupId.set(id);
  }

  isPathActive(path: string): boolean {
    return this.matchesPath(path, this.currentPath());
  }

  private matchesPath(itemPath: string, current: string): boolean {
    if (itemPath === '/') return current === '/' || current === '';
    return current === itemPath || current.startsWith(itemPath + '/');
  }

  constructor() {
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe((e) => {
        this.mobileNavOpen.set(false);
        this.currentPath.set(e.urlAfterRedirects);
        // Navigation sonrası override'ı temizle ki URL gerçek aktif grubu sürsün.
        this._activeGroupId.set(null);
      });
  }

  toggleMobileNav(): void {
    this.mobileNavOpen.update((v) => !v);
  }

  closeMobileNav(): void {
    this.mobileNavOpen.set(false);
  }

  logout(): void {
    this.auth.logout().subscribe(() => this.router.navigate(['/login']));
  }
}
