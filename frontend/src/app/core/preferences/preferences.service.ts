import { Injectable, effect, signal } from '@angular/core';

export type Language = 'tr' | 'en';
export type DateFormat = 'tr' | 'iso' | 'us';

export interface NotificationPreferences {
  emailSayimAtamasi: boolean;
  emailOnayBekleyen: boolean;
  emailArkadaslik: boolean;
  inappSayimHareketi: boolean;
  inappCagri: boolean;
  inappSistem: boolean;
  soundEnabled: boolean;
  dndEnabled: boolean;
}

export type NotificationKey = keyof NotificationPreferences;

export interface Preferences {
  language: Language;
  dateFormat: DateFormat;
  reduceMotion: boolean;
  notifications: NotificationPreferences;
}

const STORAGE_KEY = 'syncompare.preferences';
const DEFAULT_NOTIFICATIONS: NotificationPreferences = {
  emailSayimAtamasi: true,
  emailOnayBekleyen: true,
  emailArkadaslik: true,
  inappSayimHareketi: true,
  inappCagri: true,
  inappSistem: true,
  soundEnabled: true,
  dndEnabled: false,
};
const DEFAULT_PREFERENCES: Preferences = {
  language: 'tr',
  dateFormat: 'tr',
  reduceMotion: false,
  notifications: DEFAULT_NOTIFICATIONS,
};

@Injectable({ providedIn: 'root' })
export class PreferencesService {
  private readonly _prefs = signal<Preferences>(this.readInitial());
  readonly prefs = this._prefs.asReadonly();

  constructor() {
    effect(() => {
      const p = this._prefs();
      try { localStorage.setItem(STORAGE_KEY, JSON.stringify(p)); } catch { /* private mode */ }
      // Reduce-motion is the only side-effect we actually apply globally for now —
      // a data attribute on <html> that CSS can use to disable animations.
      document.documentElement.dataset['reduceMotion'] = p.reduceMotion ? 'true' : 'false';
    });
  }

  setLanguage(language: Language): void {
    this._prefs.update((p) => ({ ...p, language }));
  }

  setDateFormat(dateFormat: DateFormat): void {
    this._prefs.update((p) => ({ ...p, dateFormat }));
  }

  setReduceMotion(reduceMotion: boolean): void {
    this._prefs.update((p) => ({ ...p, reduceMotion }));
  }

  setNotification(key: NotificationKey, value: boolean): void {
    this._prefs.update((p) => ({
      ...p,
      notifications: { ...p.notifications, [key]: value },
    }));
  }

  resetNotifications(): void {
    this._prefs.update((p) => ({ ...p, notifications: DEFAULT_NOTIFICATIONS }));
  }

  /** Format a date with the user's chosen pattern. Useful from anywhere. */
  formatDate(d: Date | string | null | undefined): string {
    if (!d) return '';
    const date = d instanceof Date ? d : new Date(d);
    if (isNaN(date.getTime())) return '';
    const dd = String(date.getDate()).padStart(2, '0');
    const mm = String(date.getMonth() + 1).padStart(2, '0');
    const yyyy = String(date.getFullYear());
    switch (this._prefs().dateFormat) {
      case 'iso': return `${yyyy}-${mm}-${dd}`;
      case 'us':  return `${mm}/${dd}/${yyyy}`;
      case 'tr':
      default:    return `${dd}.${mm}.${yyyy}`;
    }
  }

  private readInitial(): Preferences {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return DEFAULT_PREFERENCES;
      const parsed = JSON.parse(raw) as Partial<Preferences>;
      const notifications = { ...DEFAULT_NOTIFICATIONS, ...(parsed.notifications ?? {}) };
      return {
        language: parsed.language === 'en' ? 'en' : 'tr',
        dateFormat: parsed.dateFormat === 'iso' || parsed.dateFormat === 'us' ? parsed.dateFormat : 'tr',
        reduceMotion: parsed.reduceMotion === true,
        notifications,
      };
    } catch {
      return DEFAULT_PREFERENCES;
    }
  }
}
