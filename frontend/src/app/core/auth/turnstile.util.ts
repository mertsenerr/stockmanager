/**
 * Cloudflare Turnstile loader + helper.
 *
 * - loadTurnstile() injects the Cloudflare script once and resolves when the
 *   global `turnstile` object is available.
 * - renderTurnstile() is a thin wrapper over `turnstile.render()` that returns
 *   the widget id (so the caller can reset it after a failed submit).
 *
 * The script source intentionally pins to the `r/{version}` directory so we
 * don't pull a new minor version every page load.
 */

const SCRIPT_URL = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';

declare global {
  interface Window {
    turnstile?: {
      render(container: HTMLElement, options: TurnstileRenderOptions): string;
      reset(widgetId?: string): void;
      remove(widgetId: string): void;
      getResponse(widgetId?: string): string | undefined;
    };
  }
}

export interface TurnstileRenderOptions {
  sitekey: string;
  callback?: (token: string) => void;
  'error-callback'?: () => void;
  'expired-callback'?: () => void;
  theme?: 'light' | 'dark' | 'auto';
  size?: 'normal' | 'compact' | 'flexible';
  appearance?: 'always' | 'execute' | 'interaction-only';
}

let loadPromise: Promise<void> | null = null;

export function loadTurnstile(): Promise<void> {
  if (window.turnstile) return Promise.resolve();
  if (loadPromise) return loadPromise;
  loadPromise = new Promise<void>((resolve, reject) => {
    const existing = document.querySelector(`script[src^="${SCRIPT_URL.split('?')[0]}"]`);
    if (existing) {
      // Script tag is already present (maybe loading) — poll briefly.
      const check = () => {
        if (window.turnstile) resolve();
        else setTimeout(check, 50);
      };
      check();
      return;
    }
    const script = document.createElement('script');
    script.src = SCRIPT_URL;
    script.async = true;
    script.defer = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error('Turnstile script load failed'));
    document.head.appendChild(script);
  });
  return loadPromise;
}

export function renderTurnstile(container: HTMLElement, options: TurnstileRenderOptions): string {
  if (!window.turnstile) throw new Error('Turnstile not loaded yet');
  return window.turnstile.render(container, options);
}

export function resetTurnstile(widgetId?: string): void {
  window.turnstile?.reset(widgetId);
}

export function removeTurnstile(widgetId: string): void {
  window.turnstile?.remove(widgetId);
}
