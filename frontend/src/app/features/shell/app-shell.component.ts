import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ROLE_LABELS, UserRole } from '../../core/auth/auth.models';
import { ThemeService } from '../../core/theme/theme.service';
import { ToastHostComponent } from '../../shared/ui/toast/toast-host.component';
import { ConfirmHostComponent } from '../../shared/ui/confirm/confirm-host.component';
import { IncomingCallService } from '../../core/realtime/incoming-call.service';
import { IncomingCallHostComponent } from '../../core/realtime/incoming-call-host.component';

interface SubItem {
  label: string;
  path: string;
  description: string;
  badge?: string;
  roles?: UserRole[];
}

interface NavGroup {
  id: string;
  label: string;
  icon: string;
  description: string;
  items: SubItem[];
  roles?: UserRole[];
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, ToastHostComponent, ConfirmHostComponent, IncomingCallHostComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app-shell.component.html',
  styleUrls: ['./app-shell.component.css'],
})
export class AppShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly incomingCalls = inject(IncomingCallService);
  private readonly themeSvc = inject(ThemeService);

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

  private readonly allGroups: NavGroup[] = [
    {
      id: 'home',
      label: 'Anasayfa',
      icon: '◎',
      description: 'Hızlı bakış',
      items: [
        { label: 'Anasayfa', path: '/', description: 'Hesap & sistem özet' },
      ],
    },
    {
      id: 'compare',
      label: 'Karşılaştırma',
      icon: '◐',
      description: 'Sayım & raporlar',
      items: [
        { label: 'Oturumlar', path: '/oturumlar', description: 'Aktif & geçmiş canlı sayımlar' },
        { label: 'Özel Raporlar', path: '/ozel-raporlar', description: 'Excel/PDF paylaşımlı raporlar' },
      ],
    },
    {
      id: 'data',
      label: 'Veri',
      icon: '◇',
      description: 'Firmalar & mağazalar',
      roles: ['Sistem', 'SayimBaskani'],
      items: [
        { label: 'Firmalar', path: '/firmalar', description: 'Müşteri firma listesi' },
        { label: 'Mağazalar', path: '/magazalar', description: 'Şube ağı' },
      ],
    },
    {
      id: 'people',
      label: 'Hesap',
      icon: '☻',
      description: 'Kullanıcılar & arkadaşlar',
      items: [
        { label: 'Arkadaşlar', path: '/arkadaslar', description: 'Arkadaş listesi & istekler' },
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
