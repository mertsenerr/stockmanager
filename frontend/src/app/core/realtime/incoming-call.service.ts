import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { SayimHubService } from '../../features/sayim/live/sayim-hub.service';

export interface IncomingCall {
  oturumId: string;
  baslatanKullaniciId: string;
  baslatanAdi: string;
  receivedAt: number;
}

/**
 * Global gelen-arama bildirim servisi.
 * Hub'tan `callRinging$` dinler, kullanıcı hangi sayfada olursa olsun pop-up için signal sağlar.
 * Pop-up UI'sı `IncomingCallHostComponent` tarafından AppShell'de render edilir.
 */
@Injectable({ providedIn: 'root' })
export class IncomingCallService {
  private readonly hub = inject(SayimHubService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly incoming = signal<IncomingCall | null>(null);
  readonly visible = computed(() => this.incoming() !== null);

  /**
   * Davet kabul edilince hedef oturum + mode burada tutulur.
   * OturumDetailComponent bu signal'ı izler; kendi oturumu ise modal'ı aç + autostart.
   * Aynı sayfadaki davet kabulü router.navigate skip etse bile yakalanır.
   */
  readonly pendingCall = signal<{ oturumId: string; mode: 'audio' | 'video' } | null>(null);

  private timer: ReturnType<typeof setTimeout> | null = null;
  private ringtone: HTMLAudioElement | null = null;
  private subscribed = false;
  private connectScheduled = false;

  constructor() {
    // Kullanıcı oturum açtığında hub'ı kur ve callRinging dinleyicisini bir kere başlat.
    effect(() => {
      const u = this.auth.currentUser();
      if (u && !this.connectScheduled) {
        this.connectScheduled = true;
        this.hub.connect().catch(() => undefined);
        this.subscribeOnce();
      } else if (!u) {
        this.dismiss();
        this.connectScheduled = false;
        this.hub.disconnect().catch(() => undefined);
      }
    });
  }

  private subscribeOnce(): void {
    if (this.subscribed) return;
    this.subscribed = true;
    this.hub.callRinging$.subscribe((e) => {
      const me = this.auth.currentUser()?.id;
      // Başlatan kendisi → gösterme.
      if (e.baslatanKullaniciId === me) return;
      // Aynı oturum için zaten gösterilen bir bildirim varsa yenile.
      this.incoming.set({
        oturumId: e.oturumId,
        baslatanKullaniciId: e.baslatanKullaniciId,
        baslatanAdi: e.baslatanKullaniciAdi,
        receivedAt: Date.now(),
      });
      this.playRingtone();
      this.flashTitle(`📞 ${e.baslatanKullaniciAdi} arıyor...`);
      if (this.timer) clearTimeout(this.timer);
      this.timer = setTimeout(() => this.dismiss(), 30_000);
    });
  }

  private originalTitle: string | null = null;
  private titleInterval: ReturnType<typeof setInterval> | null = null;
  private flashTitle(msg: string): void {
    if (typeof document === 'undefined') return;
    if (this.originalTitle === null) this.originalTitle = document.title;
    let toggle = false;
    if (this.titleInterval) clearInterval(this.titleInterval);
    this.titleInterval = setInterval(() => {
      document.title = toggle ? msg : (this.originalTitle ?? 'SayımLink');
      toggle = !toggle;
    }, 1000);
  }
  private restoreTitle(): void {
    if (this.titleInterval) {
      clearInterval(this.titleInterval);
      this.titleInterval = null;
    }
    if (this.originalTitle !== null && typeof document !== 'undefined') {
      document.title = this.originalTitle;
    }
  }

  accept(mode: 'audio' | 'video'): void {
    const cur = this.incoming();
    this.dismiss();
    if (!cur) return;
    // pendingCall signal: detay komponenti same-page navigation'da bile bunu yakalar.
    this.pendingCall.set({ oturumId: cur.oturumId, mode });
    // Detay sayfasında değilsek navigate; aynı sayfadaysak signal effect'i halleder.
    this.router.navigate(['/oturumlar', cur.oturumId]);
  }

  consumePendingCall(): { oturumId: string; mode: 'audio' | 'video' } | null {
    const p = this.pendingCall();
    if (p) this.pendingCall.set(null);
    return p;
  }

  dismiss(): void {
    this.incoming.set(null);
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = null;
    }
    this.stopRingtone();
    this.restoreTitle();
  }

  private playRingtone(): void {
    try {
      if (!this.ringtone) {
        this.ringtone = new Audio(
          'data:audio/wav;base64,UklGRiQEAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAEAAB/f39/f39/f39/f39/f39/f3+AgIB/f3+AgIB/f3+AgIB/f3+AgIB/f3+AgIA=',
        );
        this.ringtone.loop = true;
        this.ringtone.volume = 0.4;
      }
      this.ringtone.currentTime = 0;
      this.ringtone.play().catch(() => undefined);
    } catch { /* noop */ }
  }

  private stopRingtone(): void {
    try { this.ringtone?.pause(); } catch { /* noop */ }
  }
}
