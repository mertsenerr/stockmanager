import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnDestroy,
  afterNextRender,
  inject,
  output,
  signal,
} from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { ThemeService } from '../../../core/theme/theme.service';
import {
  loadTurnstile,
  removeTurnstile,
  renderTurnstile,
  resetTurnstile,
} from '../../../core/auth/turnstile.util';

/**
 * Drop-in Cloudflare Turnstile widget.
 *
 *   <app-turnstile (token)="onCaptchaToken($event)" />
 *
 * - Hidden completely when the backend has Turnstile disabled.
 * - Emits the token whenever the user solves the challenge (or auto-passes).
 * - Calls reset() between submits via the public reset() method.
 */
@Component({
  selector: 'app-turnstile',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <div #host class="my-2"></div>
    }
  `,
})
export class TurnstileComponent implements AfterViewInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly themeSvc = inject(ThemeService);
  private readonly elementRef = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);

  readonly token = output<string>();
  readonly errored = output<void>();

  protected readonly visible = signal(false);

  private widgetId: string | null = null;
  private siteKey = '';

  ngAfterViewInit(): void {
    this.auth.captchaConfig().subscribe({
      next: (cfg) => {
        if (!cfg.enabled || !cfg.siteKey) return;
        this.siteKey = cfg.siteKey;
        this.visible.set(true);
        // Wait for the next render so the @if {} host div is in the DOM.
        // queueMicrotask isn't enough: on warm visits loadTurnstile() resolves
        // synchronously and mount() ran before Angular flushed change detection,
        // so querySelector returned null and the widget never rendered.
        afterNextRender(() => this.mount(), { injector: this.injector });
      },
      error: () => {/* fail open — user can still submit; backend will reject if it really needed token */},
    });
  }

  ngOnDestroy(): void {
    if (this.widgetId) removeTurnstile(this.widgetId);
  }

  reset(): void {
    if (this.widgetId) resetTurnstile(this.widgetId);
  }

  private async mount(): Promise<void> {
    try {
      await loadTurnstile();
      const host = this.elementRef.nativeElement.querySelector('div') as HTMLElement | null;
      if (!host) return;
      this.widgetId = renderTurnstile(host, {
        sitekey: this.siteKey,
        theme: this.themeSvc.theme() === 'dark' ? 'dark' : 'light',
        size: 'flexible',
        callback: (token) => this.token.emit(token),
        'error-callback': () => this.errored.emit(),
        'expired-callback': () => this.errored.emit(),
      });
    } catch {
      // Script load failed — silently hide; backend may still reject if required.
      this.visible.set(false);
    }
  }
}
