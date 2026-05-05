import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { PageHeaderComponent } from '../../shared/ui/page-header/page-header.component';
import { ToastService } from '../../shared/ui/toast/toast.service';
import { ConfirmService } from '../../shared/ui/confirm/confirm.service';
import { ArkadasService, Friend, UserSearchResult } from './arkadas.service';
import { SayimHubService } from '../sayim/live/sayim-hub.service';
import { CallService } from '../sayim/live/call.service';
import { ToastService as _ } from '../../shared/ui/toast/toast.service';

@Component({
  selector: 'app-arkadaslar',
  standalone: true,
  imports: [PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="Arkadaşlar" description="Arkadaş ekle, gelen istekleri yönet, aktif sayım aramalarına davet et."></app-page-header>

    <div class="grid gap-4 grid-cols-1 lg:grid-cols-2">
      <!-- Arkadaş ara / ekle -->
      <section class="card-flat p-4">
        <h3 class="font-mono text-[10px] uppercase tracking-wider text-ink-muted mb-2">Arkadaş ekle</h3>
        <input type="search"
               [value]="searchQuery()"
               (input)="onSearchInput(($any($event.target)).value)"
               placeholder="Ad veya e-posta ile ara (en az 2 karakter)..."
               class="field-input mb-3" />

        @if (searching()) {
          <p class="text-xs text-ink-muted text-center py-4">Aranıyor...</p>
        } @else if (searchResults().length > 0) {
          <ul class="space-y-1.5 max-h-[40vh] overflow-y-auto -mx-2 px-2">
            @for (u of searchResults(); track u.id) {
              <li class="flex items-center gap-2 p-2 rounded-lg border-[1.5px] border-ink/20 hover:border-ink transition-colors">
                <div class="w-8 h-8 rounded-full border-[1.5px] border-ink bg-mint/40 flex items-center justify-center text-[10px] font-bold font-mono shrink-0">
                  {{ u.adSoyad.slice(0, 2).toUpperCase() }}
                </div>
                <div class="flex-1 min-w-0">
                  <p class="text-xs font-semibold truncate">{{ u.adSoyad }}</p>
                  <p class="text-[11px] text-ink-muted truncate">{{ u.email }} · {{ u.rol }}</p>
                </div>
                @switch (u.arkadaslikDurumu) {
                  @case ('arkadas') {
                    <span class="font-mono text-[10px] uppercase tracking-wider px-2 py-1 rounded shrink-0"
                          style="background: var(--color-mint);">Arkadaş</span>
                  }
                  @case ('giden') {
                    <span class="font-mono text-[10px] uppercase tracking-wider px-2 py-1 rounded shrink-0"
                          style="background: var(--color-butter);">Gönderildi</span>
                  }
                  @case ('gelen') {
                    <span class="font-mono text-[10px] uppercase tracking-wider px-2 py-1 rounded shrink-0"
                          style="background: var(--color-cobalt); color: #fff;">Gelen istek</span>
                  }
                  @default {
                    <button type="button" (click)="sendRequest(u)"
                            [disabled]="sending() === u.id"
                            class="btn btn-xs btn-primary shrink-0 disabled:opacity-50">
                      {{ sending() === u.id ? '...' : '+ Ekle' }}
                    </button>
                  }
                }
              </li>
            }
          </ul>
        } @else if (searchQuery().trim().length >= 2) {
          <p class="text-xs text-ink-muted text-center py-4 italic">Eşleşen kullanıcı yok.</p>
        } @else {
          <p class="text-xs text-ink-muted text-center py-4 italic">Aramaya başla...</p>
        }
      </section>

      <!-- Gelen istekler -->
      <section class="card-flat p-4">
        <h3 class="font-mono text-[10px] uppercase tracking-wider text-ink-muted mb-2">
          Gelen istekler ({{ incoming().length }})
        </h3>
        @if (incoming().length === 0) {
          <p class="text-xs text-ink-muted text-center py-4 italic">Bekleyen istek yok.</p>
        } @else {
          <ul class="space-y-1.5">
            @for (f of incoming(); track f.id) {
              <li class="flex items-center gap-2 p-2 rounded-lg border-[1.5px] border-ink/20">
                <div class="w-8 h-8 rounded-full border-[1.5px] border-ink bg-cobalt/30 flex items-center justify-center text-[10px] font-bold font-mono shrink-0">
                  {{ f.adSoyad.slice(0, 2).toUpperCase() }}
                </div>
                <div class="flex-1 min-w-0">
                  <p class="text-xs font-semibold truncate">{{ f.adSoyad }}</p>
                  <p class="text-[11px] text-ink-muted truncate">{{ f.email }}</p>
                </div>
                <button type="button" (click)="accept(f)" class="btn btn-xs btn-primary shrink-0">Kabul</button>
                <button type="button" (click)="reject(f)" class="btn btn-xs btn-ghost shrink-0">Red</button>
              </li>
            }
          </ul>
        }
      </section>

      <!-- Arkadaşlarım -->
      <section class="card-flat p-4 lg:col-span-2">
        <div class="flex items-center justify-between mb-2">
          <h3 class="font-mono text-[10px] uppercase tracking-wider text-ink-muted">
            Arkadaşlarım ({{ friends().length }})
          </h3>
          @if (outgoing().length > 0) {
            <span class="font-mono text-[10px] text-ink-muted">{{ outgoing().length }} bekleyen istek gönderildi</span>
          }
        </div>
        @if (loading()) {
          <p class="text-xs text-ink-muted text-center py-6">Yükleniyor...</p>
        } @else if (friends().length === 0) {
          <p class="text-xs text-ink-muted text-center py-6 italic">Henüz arkadaşın yok. Yukarıdan birini ara.</p>
        } @else {
          <ul class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-2">
            @for (f of friends(); track f.id) {
              <li class="flex items-center gap-2 p-2 rounded-lg border-[1.5px] border-ink/20 hover:border-ink transition-colors">
                <div class="w-8 h-8 rounded-full border-[1.5px] border-ink bg-mint/40 flex items-center justify-center text-[10px] font-bold font-mono shrink-0">
                  {{ f.adSoyad.slice(0, 2).toUpperCase() }}
                </div>
                <div class="flex-1 min-w-0">
                  <p class="text-xs font-semibold truncate">{{ f.adSoyad }}</p>
                  <p class="text-[11px] text-ink-muted truncate">{{ f.email }} · {{ f.rol }}</p>
                </div>
                <button type="button" (click)="remove(f)" class="focus-ring text-ink-muted hover:text-coral text-lg leading-none shrink-0"
                        title="Arkadaşlıktan çıkar">×</button>
              </li>
            }
          </ul>
        }
      </section>
    </div>
  `,
})
export class ArkadaslarComponent implements OnInit {
  private readonly svc = inject(ArkadasService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(true);
  readonly friends = signal<Friend[]>([]);
  readonly incoming = signal<Friend[]>([]);
  readonly outgoing = signal<Friend[]>([]);

  readonly searchQuery = signal('');
  readonly searching = signal(false);
  readonly searchResults = signal<UserSearchResult[]>([]);
  readonly sending = signal<string | null>(null);

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.refresh();
  }

  private refresh(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (res) => {
        this.friends.set(res.arkadaslar);
        this.incoming.set(res.gelenIstekler);
        this.outgoing.set(res.gidenIstekler);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Arkadaş listesi yüklenemedi.');
      },
    });
  }

  onSearchInput(v: string): void {
    this.searchQuery.set(v);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    const q = v.trim();
    if (q.length < 2) {
      this.searchResults.set([]);
      this.searching.set(false);
      return;
    }
    this.searching.set(true);
    this.searchTimer = setTimeout(() => {
      this.svc.search(q).subscribe({
        next: (list) => {
          this.searchResults.set(list);
          this.searching.set(false);
        },
        error: () => {
          this.searching.set(false);
          this.toast.error('Arama başarısız.');
        },
      });
    }, 300);
  }

  sendRequest(u: UserSearchResult): void {
    this.sending.set(u.id);
    this.svc.sendRequest(u.id).subscribe({
      next: () => {
        this.sending.set(null);
        this.toast.success(`${u.adSoyad} kullanıcısına istek gönderildi.`);
        // Local state güncelle
        this.searchResults.update((list) =>
          list.map((x) => x.id === u.id ? { ...x, arkadaslikDurumu: 'giden' as const } : x),
        );
        this.refresh();
      },
      error: (err: HttpErrorResponse) => {
        this.sending.set(null);
        this.toast.error(err.error?.message ?? 'İstek gönderilemedi.');
      },
    });
  }

  accept(f: Friend): void {
    this.svc.accept(f.id).subscribe({
      next: () => { this.toast.success(`${f.adSoyad} arkadaş listene eklendi.`); this.refresh(); },
      error: () => this.toast.error('İstek kabul edilemedi.'),
    });
  }

  reject(f: Friend): void {
    this.svc.reject(f.id).subscribe({
      next: () => { this.toast.success('İstek reddedildi.'); this.refresh(); },
      error: () => this.toast.error('İstek reddedilemedi.'),
    });
  }

  async remove(f: Friend): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Arkadaşlıktan çıkar',
      message: `${f.adSoyad} arkadaşlık listenden kaldırılsın mı?`,
      confirmLabel: 'Çıkar',
      cancelLabel: 'Vazgeç',
      danger: true,
    });
    if (!ok) return;
    this.svc.remove(f.kullaniciId).subscribe({
      next: () => { this.toast.success('Arkadaşlıktan çıkarıldı.'); this.refresh(); },
      error: () => this.toast.error('Çıkarma başarısız.'),
    });
  }
}
