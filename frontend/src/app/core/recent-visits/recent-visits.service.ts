import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../auth/auth.service';

const MAX_VISITS = 5;
const STORAGE_PREFIX = 'syncompare.recentVisits';
const IGNORED_PREFIXES = ['/login', '/register', '/forgot-password', '/reset-password', '/verify-email'];

@Injectable({ providedIn: 'root' })
export class RecentVisitsService {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  private readonly _visits = signal<string[]>([]);
  readonly visits = computed(() => this._visits());

  constructor() {
    effect(() => {
      const user = this.auth.currentUser();
      this._visits.set(user ? this.load(user.id) : []);
    });

    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe((e) => this.record(e.urlAfterRedirects));
  }

  private record(url: string): void {
    if (!this.auth.currentUser() || this.shouldIgnore(url)) return;
    const path = this.stripQuery(url);
    const next = [path, ...this._visits().filter((u) => u !== path)].slice(0, MAX_VISITS);
    this._visits.set(next);
    const userId = this.auth.currentUser()?.id;
    if (userId) this.save(userId, next);
  }

  private stripQuery(url: string): string {
    const q = url.indexOf('?');
    return q >= 0 ? url.slice(0, q) : url;
  }

  private shouldIgnore(url: string): boolean {
    if (!url || url === '/' || url === '') return true;
    return IGNORED_PREFIXES.some((p) => url === p || url.startsWith(p + '/') || url.startsWith(p + '?'));
  }

  private storageKey(userId: string): string {
    return `${STORAGE_PREFIX}.${userId}`;
  }

  private load(userId: string): string[] {
    try {
      const raw = localStorage.getItem(this.storageKey(userId));
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed)
        ? parsed.filter((v): v is string => typeof v === 'string').slice(0, MAX_VISITS)
        : [];
    } catch {
      return [];
    }
  }

  private save(userId: string, visits: string[]): void {
    try {
      localStorage.setItem(this.storageKey(userId), JSON.stringify(visits));
    } catch {
      // localStorage may be unavailable (private mode, quota). Silently skip.
    }
  }
}
