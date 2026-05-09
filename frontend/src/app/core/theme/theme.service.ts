import { Injectable, effect, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark';

const STORAGE_KEY = 'syncompare.theme';
const DEFAULT_THEME: ThemeMode = 'dark';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly _theme = signal<ThemeMode>(this.readInitial());
  readonly theme = this._theme.asReadonly();

  constructor() {
    effect(() => {
      const t = this._theme();
      const root = document.documentElement;
      root.dataset['theme'] = t;
      root.classList.toggle('dark', t === 'dark');
      try { localStorage.setItem(STORAGE_KEY, t); } catch { /* private mode */ }
    });
  }

  toggle(): void {
    this._theme.update((t) => (t === 'dark' ? 'light' : 'dark'));
  }

  set(t: ThemeMode): void {
    this._theme.set(t);
  }

  private readInitial(): ThemeMode {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved === 'light' || saved === 'dark') return saved;
    } catch { /* ignore */ }
    return DEFAULT_THEME;
  }
}
