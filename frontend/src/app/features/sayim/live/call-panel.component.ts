import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, HostListener, Input, OnDestroy, ViewChild, computed, inject, signal } from '@angular/core';
import { CallService } from './call.service';
import { SrcObjectDirective } from './src-object.directive';
import { SayimHubService } from './sayim-hub.service';
import { InvitableUser, OturumService } from '../oturum.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { AuthService } from '../../../core/auth/auth.service';
import { ArkadasService, Friend } from '../../arkadaslar/arkadas.service';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-call-panel',
  standalone: true,
  imports: [SrcObjectDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    .call-panel-host {
      transition: width 220ms ease, box-shadow 220ms ease;
    }
    .floating-pip {
      position: fixed !important;
      max-width: 90vw;
      z-index: 80;
      box-shadow: 0 14px 32px rgba(0,0,0,0.32);
      animation: pip-enter 320ms cubic-bezier(0.2, 0.9, 0.3, 1.2);
      cursor: default;
    }
    .floating-pip .drag-handle { cursor: grab; touch-action: none; user-select: none; }
    .floating-pip.dragging .drag-handle { cursor: grabbing; }
    @keyframes pip-enter {
      from { opacity: 0; transform: translate(120px, 120px) scale(0.6); }
      to { opacity: 1; transform: translate(0, 0) scale(1); }
    }
  `],
  template: `
    <div #sentinel aria-hidden="true" style="height: 0; margin: 0; padding: 0; pointer-events: none;"></div>
    @if (floating() && reservedHeight()) {
      <div aria-hidden="true" [style.height.px]="reservedHeight()" class="mb-4"></div>
    }
    <div #panelRoot class="card-flat p-3 mb-4 call-panel-host"
         [class.floating-pip]="floating()"
         [class.dragging]="dragging()"
         [style.width.px]="floating() ? floatingWidthPx() : null"
         [style.left.px]="floating() && pipPos() ? pipPos()!.left : null"
         [style.top.px]="floating() && pipPos() ? pipPos()!.top : null"
         [style.right.px]="floating() && !pipPos() ? 16 : null"
         [style.bottom.px]="floating() && !pipPos() ? 16 : null"
         style="background: var(--color-surface);">
      <div class="flex items-center justify-between mb-3 flex-wrap gap-2 drag-handle"
           (pointerdown)="onDragStart($event)"
           (pointermove)="onDragMove($event)"
           (pointerup)="onDragEnd($event)"
           (pointercancel)="onDragEnd($event)">
        <div class="flex items-center gap-2">
          <span class="font-mono text-[10px] uppercase tracking-wider text-ink-muted">Görüntülü / Sesli Arama</span>
          @if (call.active()) {
            <span class="chip">
              <span class="w-1.5 h-1.5 rounded-full bg-coral animate-pulse"></span>
              {{ call.mode() === 'video' ? 'Görüntülü' : 'Sesli' }} · {{ totalCount() }} katılımcı
            </span>
          }
        </div>
        <div class="flex items-center gap-2 flex-wrap">
          @if (!call.active()) {
            <button type="button" (click)="start('video')" class="btn btn-sm btn-primary">▶ Görüntülü başlat</button>
            <button type="button" (click)="start('audio')" class="btn btn-sm btn-ghost">🎙 Sesli başlat</button>
            @if (canInvite()) {
              <button type="button" (click)="openInviteModal()" class="btn btn-sm btn-ghost">👥 Kullanıcı ekle</button>
            }
          } @else {
            <button type="button" (click)="compact.set(!compact())" class="btn btn-sm btn-ghost">
              {{ compact() ? '⤢ Büyüt' : '⤡ Küçült' }}
            </button>
            <button type="button" (click)="call.toggleMic()" class="btn btn-sm btn-ghost">
              {{ call.micMuted() ? '🔇 Mikrofonu aç' : '🎙 Mikrofonu kapat' }}
            </button>
            @if (canInvite()) {
              <button type="button" (click)="openInviteModal()" class="btn btn-sm btn-ghost">👥 Kullanıcı ekle</button>
            }
            @if (call.mode() === 'video') {
              <button type="button" (click)="call.toggleCam()" class="btn btn-sm btn-ghost">
                {{ call.camOff() ? '📷 Kamerayı aç' : '📷 Kamerayı kapat' }}
              </button>
              @if (!call.screenSharing()) {
                <button type="button" (click)="call.startScreenShare()" class="btn btn-sm btn-ghost">🖥 Ekranı paylaş</button>
              } @else {
                <button type="button" (click)="call.stopScreenShare()" class="btn btn-sm btn-ghost">🖥 Paylaşımı durdur</button>
              }
            }
            <button type="button" (click)="call.stop()" class="btn btn-sm" style="background: var(--color-coral); color: #fff;">■ Bitir</button>
          }
        </div>
      </div>

      @if (call.errorMsg()) {
        <p class="text-[11px] text-coral mb-2">{{ call.errorMsg() }}</p>
      }

      @if (!call.active()) {
        <p class="text-[11px] text-ink-muted">
          Başlat'a basınca tarayıcı izin isteyecek. Tüm modern Windows / macOS / iOS / Android tarayıcılarında çalışır
          (HTTPS gerekir — Render zaten HTTPS).
        </p>
      } @else {
        <!-- Video grid: her kart 16:9 dikdörtgen; compact'ta grid maxWidth ile küçük kutular, expand'te full width -->
        <div class="grid gap-2"
             [style.gridTemplateColumns]="gridCols()"
             [style.maxWidth]="gridMaxWidth()">
          <!-- Kendi önizleme — ekran paylaşımı varken kart 16:9 yerine geniş ve object-contain (kırpma yok). -->
          <div class="relative rounded-lg border-[1.5px] border-ink overflow-hidden cursor-zoom-in transition-transform hover:scale-[1.01]"
               [style.aspect-ratio]="call.screenSharing() ? '16/10' : '16/9'"
               style="background: #0d0d0d;"
               (click)="openSpotlight('self')"
               title="Büyütmek için tıkla">
            @if (call.mode() === 'video' && !call.camOff()) {
              <video
                [appSrcObject]="call.localStreamSig()"
                autoplay playsinline muted
                class="w-full h-full"
                [class.object-cover]="!call.screenSharing()"
                [class.object-contain]="call.screenSharing()"></video>
            } @else {
              <div class="absolute inset-0 flex items-center justify-center text-center" style="color: #888;">
                <div>
                  <div class="text-3xl mb-1">{{ call.mode() === 'video' ? '📷' : '🎙' }}</div>
                  <p class="text-[11px] font-mono uppercase tracking-wider">{{ call.camOff() ? 'Kamera kapalı' : (call.mode() === 'audio' ? 'Sesli' : '—') }}</p>
                </div>
              </div>
            }
            <span class="absolute bottom-1.5 left-1.5 font-mono text-[10px] px-1.5 py-0.5 rounded"
                  style="background: rgba(0,0,0,0.6); color: #fff;">
              Sen {{ call.micMuted() ? '· 🔇' : '' }}
            </span>
          </div>

          <!-- Uzak katılımcılar -->
          @for (r of call.remotes(); track r.connectionId) {
            <div class="relative rounded-lg border-[1.5px] border-ink overflow-hidden cursor-zoom-in transition-transform hover:scale-[1.01]"
                 style="background: #0d0d0d; aspect-ratio: 16/9;"
                 (click)="openSpotlight(r.connectionId)"
                 title="Büyütmek için tıkla">
              <video
                [appSrcObject]="r.stream"
                autoplay playsinline
                class="w-full h-full object-cover"></video>
              <span class="absolute bottom-1.5 left-1.5 font-mono text-[10px] px-1.5 py-0.5 rounded"
                    style="background: rgba(0,0,0,0.6); color: #fff;">
                {{ r.kullaniciAdi }}
              </span>
              <span class="absolute top-1.5 right-1.5 w-2 h-2 rounded-full"
                    [class.bg-mint]="r.connectionState === 'connected'"
                    [class.bg-butter]="r.connectionState === 'connecting' || r.connectionState === 'new'"
                    [class.bg-coral]="r.connectionState === 'failed' || r.connectionState === 'disconnected' || r.connectionState === 'closed'"></span>
            </div>
          }
        </div>

        <!-- Cihaz seçimi -->
        @if (call.devices().cameras.length + call.devices().mics.length > 0) {
          <div class="flex items-center gap-2 flex-wrap mt-3">
            @if (call.mode() === 'video' && call.devices().cameras.length > 1) {
              <select class="field-input" style="padding: 6px 8px; font-size: 11.5px; max-width: 220px;"
                      [value]="call.selectedCameraId() ?? ''"
                      (change)="onCameraChange(($any($event.target)).value)">
                <option value="">Kamera (otomatik)</option>
                @for (c of call.devices().cameras; track c.deviceId) {
                  <option [value]="c.deviceId">{{ c.label || 'Kamera ' + ($index + 1) }}</option>
                }
              </select>
            }
            @if (call.devices().mics.length > 1) {
              <select class="field-input" style="padding: 6px 8px; font-size: 11.5px; max-width: 220px;"
                      [value]="call.selectedMicId() ?? ''"
                      (change)="onMicChange(($any($event.target)).value)">
                <option value="">Mikrofon (otomatik)</option>
                @for (m of call.devices().mics; track m.deviceId) {
                  <option [value]="m.deviceId">{{ m.label || 'Mikrofon ' + ($index + 1) }}</option>
                }
              </select>
            }
          </div>
        }
      }
    </div>

    @if (inviteOpen()) {
      <div class="fixed inset-0 z-[55] bg-ink/40 backdrop-blur-sm flex items-center justify-center p-4"
           (click)="closeInviteModal()">
        <div class="bg-surface border-[1.5px] border-ink rounded-xl shadow-stamp w-full max-w-md p-4 flex flex-col"
             style="max-height: 80vh;"
             (click)="$event.stopPropagation()">
          <header class="flex items-center justify-between mb-3 shrink-0">
            <h3 class="font-display font-bold text-base tracking-tight">Aramaya davet et</h3>
            <button type="button" (click)="closeInviteModal()"
                    class="focus-ring text-ink-muted hover:text-coral text-2xl leading-none">×</button>
          </header>

          <div class="flex gap-1 mb-3 shrink-0">
            <button type="button" (click)="inviteTab.set('friends')"
                    class="btn btn-xs flex-1"
                    [class.btn-primary]="inviteTab() === 'friends'"
                    [class.btn-ghost]="inviteTab() !== 'friends'">
              ☻ Arkadaşlar ({{ friends().length }})
            </button>
            @if (canInvitable()) {
              <button type="button" (click)="inviteTab.set('all')"
                      class="btn btn-xs flex-1"
                      [class.btn-primary]="inviteTab() === 'all'"
                      [class.btn-ghost]="inviteTab() !== 'all'">
                👥 Tüm uygun kullanıcılar
              </button>
            }
          </div>

          <input type="search"
                 [value]="inviteSearch()"
                 (input)="inviteSearch.set(($any($event.target)).value)"
                 placeholder="Ad veya e-posta ara..."
                 class="field-input mb-3 shrink-0" style="padding: 7px 10px; font-size: 12.5px;" />

          <div class="flex-1 overflow-y-auto -mx-2 px-2">
            @if (inviteLoading()) {
              <p class="text-xs text-ink-muted text-center py-6">Yükleniyor...</p>
            } @else if (inviteTab() === 'friends') {
              @if (filteredFriends().length === 0) {
                <p class="text-xs text-ink-muted text-center py-6 italic">
                  @if (friends().length === 0) {
                    Henüz arkadaşın yok. <a routerLink="/arkadaslar" (click)="closeInviteModal()" class="link-dotted">Arkadaş ekle →</a>
                  } @else {
                    Aramayla eşleşen arkadaş yok.
                  }
                </p>
              } @else {
                <ul class="space-y-1.5">
                  @for (f of filteredFriends(); track f.id) {
                    <li class="flex items-center gap-2 p-2 rounded-lg border-[1.5px] border-ink/20 hover:border-ink transition-colors">
                      <div class="w-8 h-8 rounded-full border-[1.5px] border-ink bg-mint/40 flex items-center justify-center text-[10px] font-bold font-mono shrink-0">
                        {{ f.adSoyad.slice(0, 2).toUpperCase() }}
                      </div>
                      <div class="flex-1 min-w-0">
                        <p class="text-xs font-semibold truncate">{{ f.adSoyad }}</p>
                        <p class="text-[11px] text-ink-muted truncate">{{ f.email }} · {{ f.rol }}</p>
                      </div>
                      @if (invited().has(f.kullaniciId)) {
                        <span class="font-mono text-[10px] uppercase tracking-wider px-2 py-1 rounded shrink-0"
                              style="background: var(--color-mint);">✓ Davet edildi</span>
                      } @else {
                        <button type="button" (click)="inviteFriend(f)"
                                [disabled]="inviting() === f.kullaniciId"
                                class="btn btn-xs btn-primary shrink-0 disabled:opacity-50">
                          {{ inviting() === f.kullaniciId ? '...' : 'Davet et' }}
                        </button>
                      }
                    </li>
                  }
                </ul>
              }
            } @else {
              @if (filteredInvitable().length === 0) {
                <p class="text-xs text-ink-muted text-center py-6 italic">Davet edilecek kullanıcı yok.</p>
              } @else {
                <ul class="space-y-1.5">
                  @for (u of filteredInvitable(); track u.id) {
                    <li class="flex items-center gap-2 p-2 rounded-lg border-[1.5px] border-ink/20 hover:border-ink transition-colors">
                      <div class="w-8 h-8 rounded-full border-[1.5px] border-ink bg-mint/40 flex items-center justify-center text-[10px] font-bold font-mono shrink-0">
                        {{ u.adSoyad.slice(0, 2).toUpperCase() }}
                      </div>
                      <div class="flex-1 min-w-0">
                        <p class="text-xs font-semibold truncate">{{ u.adSoyad }}</p>
                        <p class="text-[11px] text-ink-muted truncate">{{ u.email }} · {{ u.rol }}</p>
                      </div>
                      @if (invited().has(u.id)) {
                        <span class="font-mono text-[10px] uppercase tracking-wider px-2 py-1 rounded shrink-0"
                              style="background: var(--color-mint);">✓ Davet edildi</span>
                      } @else {
                        <button type="button" (click)="invite(u)"
                                [disabled]="inviting() === u.id"
                                class="btn btn-xs btn-primary shrink-0 disabled:opacity-50">
                          {{ inviting() === u.id ? '...' : 'Davet et' }}
                        </button>
                      }
                    </li>
                  }
                </ul>
              }
            }
          </div>
        </div>
      </div>
    }

    @if (spotlight() && spotlightStream()) {
      <div class="fixed inset-0 z-[90] flex items-center justify-center p-4 sm:p-6"
           style="background: rgba(0, 0, 0, 0.85); backdrop-filter: blur(8px);"
           (click)="closeSpotlight()">
        <div class="relative w-full h-full flex items-center justify-center"
             (click)="$event.stopPropagation()">
          <video [appSrcObject]="spotlightStream()"
                 autoplay playsinline
                 [muted]="spotlight() === 'self'"
                 class="max-w-full max-h-full object-contain rounded-xl border-[1.5px] border-ink"
                 style="background: #0d0d0d; box-shadow: 0 24px 64px rgba(0,0,0,0.6);"></video>
          <span class="absolute bottom-4 left-4 sm:bottom-6 sm:left-6 font-mono text-xs sm:text-sm px-3 py-1.5 rounded-lg pointer-events-none"
                style="background: rgba(0,0,0,0.7); color: #fff;">
            {{ spotlightLabel() }}
          </span>
          <button type="button" (click)="closeSpotlight()"
                  class="absolute top-4 right-4 sm:top-6 sm:right-6 w-11 h-11 rounded-full flex items-center justify-center text-2xl leading-none hover:scale-110 transition-transform focus-ring"
                  style="background: rgba(0,0,0,0.7); color: #fff;"
                  aria-label="Kapat">×</button>
          <span class="absolute top-4 left-4 sm:top-6 sm:left-6 font-mono text-[10px] sm:text-xs uppercase tracking-wider px-2 py-1 rounded pointer-events-none"
                style="background: rgba(0,0,0,0.55); color: #fff;">
            ESC ile kapat
          </span>
        </div>
      </div>
    }
  `,
})
export class CallPanelComponent implements AfterViewInit, OnDestroy {
  protected readonly call = inject(CallService);
  private readonly hub = inject(SayimHubService);
  private readonly oturumService = inject(OturumService);
  private readonly arkadasService = inject(ArkadasService);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);

  @Input({ required: true }) oturumId!: string;
  @ViewChild('panelRoot') private panelRoot?: ElementRef<HTMLDivElement>;
  @ViewChild('sentinel') private sentinel?: ElementRef<HTMLDivElement>;

  protected readonly floating = signal(false);
  protected readonly dragging = signal(false);
  /** Drag ile özel konumlandırılırsa burada tutulur; null ise default sağ-alt. */
  protected readonly pipPos = signal<{ left: number; top: number } | null>(null);
  /** Floating'e geçerken panelin akıştaki yüksekliğini saklar; placeholder için. */
  protected readonly reservedHeight = signal<number | null>(null);

  private scrollEl: HTMLElement | null = null;
  private scrollTarget: EventTarget | null = null;
  private readonly onScrollBound = () => this.onScroll();
  private dragOffset: { dx: number; dy: number; pointerId: number } | null = null;

  /** Aktif görüşmede kompakt mod — kameralar 110px yüksekliğinde, sticky band üstte kalır. Varsayılan açık. */
  protected readonly compact = signal(true);

  protected readonly inviteOpen = signal(false);
  protected readonly inviteLoading = signal(false);
  protected readonly inviteSearch = signal('');
  protected readonly inviteTab = signal<'friends' | 'all'>('friends');
  protected readonly invitable = signal<InvitableUser[]>([]);
  protected readonly friends = signal<Friend[]>([]);
  protected readonly invited = signal<Set<string>>(new Set());
  protected readonly inviting = signal<string | null>(null);

  /** Spotlight modal — `'self'` veya remote connectionId; null ise kapalı. */
  protected readonly spotlight = signal<string | null>(null);

  protected readonly spotlightStream = computed<MediaStream | null>(() => {
    const id = this.spotlight();
    if (!id) return null;
    if (id === 'self') return this.call.localStreamSig();
    return this.call.remotes().find((r) => r.connectionId === id)?.stream ?? null;
  });

  protected readonly spotlightLabel = computed(() => {
    const id = this.spotlight();
    if (!id) return '';
    if (id === 'self') {
      return this.call.screenSharing() ? 'Ekran paylaşımın' : 'Sen';
    }
    return this.call.remotes().find((r) => r.connectionId === id)?.kullaniciAdi ?? '';
  });

  // Davet butonu kullanıcılara da görünsün — herkes arkadaşını çağırabilir.
  protected readonly canInvite = computed(() => true);

  // "Tüm uygun kullanıcılar" tab'ı sadece Sistem/SayimBaskani için.
  protected readonly canInvitable = computed(() => {
    const r = this.auth.currentUser()?.rol;
    return r === 'Sistem' || r === 'SayimBaskani';
  });

  protected readonly filteredInvitable = computed(() => {
    const q = this.inviteSearch().toLowerCase().trim();
    const list = this.invitable();
    if (!q) return list;
    return list.filter((u) =>
      u.adSoyad.toLowerCase().includes(q) || u.email.toLowerCase().includes(q),
    );
  });

  protected readonly filteredFriends = computed(() => {
    const q = this.inviteSearch().toLowerCase().trim();
    const list = this.friends();
    if (!q) return list;
    return list.filter((f) =>
      f.adSoyad.toLowerCase().includes(q) || f.email.toLowerCase().includes(q),
    );
  });

  openSpotlight(id: string): void {
    this.spotlight.set(id);
  }

  closeSpotlight(): void {
    this.spotlight.set(null);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.spotlight()) {
      this.closeSpotlight();
      return;
    }
    if (this.inviteOpen()) this.closeInviteModal();
  }

  openInviteModal(): void {
    this.inviteOpen.set(true);
    this.inviteSearch.set('');
    this.inviteTab.set('friends');
    this.inviteLoading.set(true);

    // Arkadaşları her durumda yükle
    this.arkadasService.list().subscribe({
      next: (res) => {
        this.friends.set(res.arkadaslar);
        // Kullanıcı SayimBaskani/Sistem ise tüm uygun listeyi de yükle
        if (this.canInvitable()) {
          this.oturumService.listInvitableUsers(this.oturumId).subscribe({
            next: (list) => {
              this.invitable.set(list);
              this.inviteLoading.set(false);
            },
            error: () => {
              this.inviteLoading.set(false);
              this.toast.error('Kullanıcı listesi yüklenemedi.');
            },
          });
        } else {
          this.inviteLoading.set(false);
        }
      },
      error: () => {
        this.inviteLoading.set(false);
        this.toast.error('Arkadaş listesi yüklenemedi.');
      },
    });
  }

  closeInviteModal(): void {
    this.inviteOpen.set(false);
  }

  ngAfterViewInit(): void {
    this.scrollEl = this.findScrollParent(this.panelRoot?.nativeElement);
    // Document scrolling element'in scroll event'i window üzerinden alınır.
    this.scrollTarget = this.scrollEl === document.scrollingElement ? window : this.scrollEl;
    this.scrollTarget?.addEventListener('scroll', this.onScrollBound, { passive: true } as AddEventListenerOptions);
  }

  ngOnDestroy(): void {
    this.scrollTarget?.removeEventListener('scroll', this.onScrollBound);
  }

  private findScrollParent(el: HTMLElement | undefined): HTMLElement | null {
    if (!el) return null;
    let cur: HTMLElement | null = el.parentElement;
    while (cur) {
      const ov = getComputedStyle(cur).overflowY;
      if (ov === 'auto' || ov === 'scroll') return cur;
      cur = cur.parentElement;
    }
    return document.scrollingElement as HTMLElement | null;
  }

  private onScroll(): void {
    if (!this.call.active() || !this.sentinel?.nativeElement || !this.scrollEl) {
      if (this.floating()) {
        this.floating.set(false);
        this.reservedHeight.set(null);
        this.pipPos.set(null);
      }
      return;
    }
    // Panelin doğal pozisyonunu, akışta kalan sentinel marker'ından oku.
    // Panel floating'e geçince position:fixed olur ve kendi rect'i aldatıcıdır;
    // sentinel her zaman akışta kaldığı için doğru eşik değerini verir.
    const sRect = this.sentinel.nativeElement.getBoundingClientRect();
    const isWindowScroll = this.scrollEl === document.scrollingElement;
    const containerTop = isWindowScroll ? 0 : this.scrollEl.getBoundingClientRect().top;
    const offset = sRect.top - containerTop;
    const isFloat = this.floating();
    // Hysteresis: geçişler arasında 60px tampon — flicker engellenir.
    if (!isFloat && offset < -40) {
      const h = this.panelRoot?.nativeElement.offsetHeight ?? null;
      this.reservedHeight.set(h);
      this.floating.set(true);
    } else if (isFloat && offset > 20) {
      this.floating.set(false);
      this.reservedHeight.set(null);
      this.pipPos.set(null);
    }
  }

  onDragStart(e: PointerEvent): void {
    if (!this.floating()) return;
    if ((e.target as HTMLElement).closest('button, select, input')) return;
    e.preventDefault();
    const rect = this.panelRoot!.nativeElement.getBoundingClientRect();
    this.dragOffset = { dx: e.clientX - rect.left, dy: e.clientY - rect.top, pointerId: e.pointerId };
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    this.dragging.set(true);
    // İlk drag'de sol/üst koordinata geçiş — ekran rect'ine göre clamp.
    this.pipPos.set({ left: rect.left, top: rect.top });
  }

  onDragMove(e: PointerEvent): void {
    if (!this.dragOffset || this.dragOffset.pointerId !== e.pointerId) return;
    const left = e.clientX - this.dragOffset.dx;
    const top = e.clientY - this.dragOffset.dy;
    // Viewport içinde tut.
    const w = this.panelRoot!.nativeElement.offsetWidth;
    const h = this.panelRoot!.nativeElement.offsetHeight;
    const maxLeft = window.innerWidth - w - 4;
    const maxTop = window.innerHeight - h - 4;
    this.pipPos.set({
      left: Math.max(4, Math.min(maxLeft, left)),
      top: Math.max(4, Math.min(maxTop, top)),
    });
  }

  onDragEnd(e: PointerEvent): void {
    if (!this.dragOffset) return;
    try { (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId); } catch { /* noop */ }
    this.dragOffset = null;
    this.dragging.set(false);
  }

  invite(u: InvitableUser): void {
    this.sendInvite(u.id, u.adSoyad);
  }

  inviteFriend(f: Friend): void {
    this.sendInvite(f.kullaniciId, f.adSoyad);
  }

  private sendInvite(userId: string, label: string): void {
    this.inviting.set(userId);
    this.hub.callInvite(this.oturumId, userId).then(() => {
      const next = new Set(this.invited());
      next.add(userId);
      this.invited.set(next);
      this.inviting.set(null);
      this.toast.success(`${label} davet edildi.`);
    }).catch(() => {
      this.inviting.set(null);
      this.toast.error('Davet gönderilemedi. (Hedef kullanıcının erişim yetkisi olmayabilir.)');
    });
  }

  protected readonly totalCount = computed(() => this.call.remotes().length + 1);

  protected readonly gridCols = computed(() => {
    const total = this.call.remotes().length + 1;
    // Ekran paylaşımı aktifse paylaşım kartı önemli — tek kolona zorla, full genişlik kazansın.
    if (this.call.screenSharing()) return '1fr';
    if (total <= 1) return '1fr';
    if (total === 2) return 'repeat(2, 1fr)';
    if (total <= 4) return 'repeat(2, 1fr)';
    return 'repeat(3, 1fr)';
  });

  /** Compact ↔ büyütülmüş ↔ floating ↔ ekran paylaşımı kombinasyonlarına göre grid genişliği. */
  protected readonly gridMaxWidth = computed(() => {
    const total = this.call.remotes().length + 1;
    const isFloat = this.floating();
    const isCompact = this.compact();
    const sharing = this.call.screenSharing();
    // Ekran paylaşımı her durumda paneldeki tüm alanı kullansın.
    if (sharing) return '100%';
    if (isFloat && isCompact) return total === 1 ? '300px' : '320px';
    if (isFloat && !isCompact) return '100%';
    if (!isCompact) return '100%';
    return total === 1 ? '320px' : '480px';
  });

  /** Floating PiP genişliği: compact 360px, büyütülmüş 720px (ekranı aşmasın diye 90vw cap zaten CSS'te). */
  protected readonly floatingWidthPx = computed(() => {
    if (this.compact() && !this.call.screenSharing()) return 360;
    return 720;
  });

  start(mode: 'audio' | 'video'): void {
    this.call.start(this.oturumId, mode).catch(() => undefined);
  }

  onCameraChange(id: string): void {
    if (id) this.call.setCamera(id);
  }

  onMicChange(id: string): void {
    if (id) this.call.setMic(id);
  }
}
