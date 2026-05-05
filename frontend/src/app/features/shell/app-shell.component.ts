import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ROLE_LABELS, UserRole } from '../../core/auth/auth.models';
import { ToastHostComponent } from '../../shared/ui/toast/toast-host.component';
import { ConfirmHostComponent } from '../../shared/ui/confirm/confirm-host.component';
import { IncomingCallService } from '../../core/realtime/incoming-call.service';
import { IncomingCallHostComponent } from '../../core/realtime/incoming-call-host.component';

interface NavItem {
  label: string;
  path: string;
  icon: string;
  color: string;
  image: string;
  preview: { title: string; desc: string };
  roles?: UserRole[];
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ToastHostComponent, ConfirmHostComponent, IncomingCallHostComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app-shell.component.html',
  styleUrls: ['./app-shell.component.css'],
})
export class AppShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  // Inject sadece — servis kendi effect'i ile auth'a bakıp hub'ı bağlar ve callRinging'i dinler.
  private readonly incomingCalls = inject(IncomingCallService);

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

  private readonly allNavItems: NavItem[] = [
    {
      label: 'Anasayfa',
      path: '/',
      icon: '◎',
      color: '#ffd66e',
      image: 'https://picsum.photos/seed/sayimlink-home/600/320',
      preview: { title: 'Hızlı bakış', desc: 'Hesabın, sistem durumu ve aktif sayım özetin.' },
    },
    {
      label: 'Firmalar',
      path: '/firmalar',
      icon: '◇',
      color: '#4f46e5',
      image: 'https://picsum.photos/seed/sayimlink-brand/600/320',
      preview: { title: 'Müşteri firmaları', desc: 'LC Waikiki, Levi\'s, BP gibi firmaları yönet.' },
      roles: ['Sistem', 'SayimBaskani'],
    },
    {
      label: 'Mağazalar',
      path: '/magazalar',
      icon: '⌂',
      color: '#ffd66e',
      image: 'https://picsum.photos/seed/sayimlink-retail/600/320',
      preview: { title: 'Mağaza ağı', desc: 'Şubeleri, lokasyonları ve harita konumlarını yönet.' },
      roles: ['Sistem', 'SayimBaskani'],
    },
    {
      label: 'Oturumlar',
      path: '/oturumlar',
      icon: '◐',
      color: '#ff5d3a',
      image: 'https://picsum.photos/seed/sayimlink-warehouse/600/320',
      preview: { title: 'Sayım oturumları', desc: 'Aktif ve geçmiş canlı sayımları yönet.' },
    },
    {
      label: 'Raporlar',
      path: '/raporlar',
      icon: '⌗',
      color: '#5cc99a',
      image: 'https://picsum.photos/seed/sayimlink-charts/600/320',
      preview: { title: 'Sapma & performans', desc: 'Geçmiş sayımları ve sapmaları analiz et.' },
      roles: ['Sistem', 'SayimBaskani'],
    },
    {
      label: 'Arkadaşlar',
      path: '/arkadaslar',
      icon: '☻',
      color: '#c7e84d',
      image: 'https://picsum.photos/seed/sayimlink-friends/600/320',
      preview: { title: 'Arkadaş listesi', desc: 'Arkadaş ekle ve sayım aramalarına davet et.' },
    },
  ];

  readonly navItems = computed(() => {
    const u = this.user();
    if (!u) return [];
    return this.allNavItems.filter(
      (item) => !item.roles || item.roles.includes(u.rol),
    );
  });

  constructor() {
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe(() => this.mobileNavOpen.set(false));
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
