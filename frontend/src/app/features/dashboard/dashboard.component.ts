import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { HealthService, HealthCheckResponse } from '../../core/services/health.service';
import { OturumService } from '../sayim/oturum.service';
import { OturumList, OTURUM_DURUM_LABELS, OturumDurum } from '../sayim/sayim.models';

interface ActivityRow {
  id: string;
  icon: string;
  title: string;
  meta: string;
  tone: 'violet' | 'cyan' | 'pink' | 'green';
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly health = inject(HealthService);
  private readonly oSvc = inject(OturumService);
  private readonly router = inject(Router);

  readonly user = this.auth.currentUser;
  readonly healthState = signal<HealthCheckResponse | null>(null);
  readonly oturumlar = signal<OturumList[]>([]);

  readonly canManage = computed(() => {
    const r = this.user()?.rol;
    return r === 'Sistem' || r === 'SayimBaskani';
  });

  readonly aktifOturumlar = computed(() =>
    this.oturumlar().filter((o) => o.durum === 'aktif' || o.durum === 'kilitli'),
  );
  readonly tamamlananOturumlar = computed(() =>
    this.oturumlar().filter((o) => o.durum === 'tamamlandi'),
  );

  /** Özet metrikler */
  readonly metrics = computed(() => {
    const list = this.oturumlar();
    const aktif = list.filter((o) => o.durum === 'aktif' || o.durum === 'kilitli').length;
    const tamamlandi = list.filter((o) => o.durum === 'tamamlandi').length;
    const taslak = list.filter((o) => o.durum === 'taslak' || o.durum === 'excel_bekleniyor').length;

    const toplamUrun = list.reduce((s, o) => s + (o.ozetler?.toplamUrun ?? 0), 0);
    const onaylanmis = list.reduce((s, o) => s + (o.ozetler?.onaylanmis ?? 0), 0);
    const oran = toplamUrun > 0 ? Math.round((onaylanmis / toplamUrun) * 100) : 0;

    return { aktif, tamamlandi, taslak, toplamUrun, onaylanmis, oran };
  });

  /** Yaklaşan oturumlar — bugünden sonraki ilk 5 */
  readonly yaklasanOturumlar = computed(() => {
    const today = new Date().toISOString().slice(0, 10);
    return this.oturumlar()
      .filter((o) => o.tarih.slice(0, 10) >= today && o.durum !== 'iptal' && o.durum !== 'tamamlandi')
      .sort((a, b) => a.tarih.localeCompare(b.tarih))
      .slice(0, 5);
  });

  /** Son aktiviteler — son 5 oturum hareketi */
  readonly recentActivity = computed<ActivityRow[]>(() => {
    return [...this.oturumlar()]
      .sort((a, b) => (b.olusturmaTarihi ?? '').localeCompare(a.olusturmaTarihi ?? ''))
      .slice(0, 5)
      .map((o) => ({
        id: o.id,
        icon: this.iconForDurum(o.durum),
        title: `${o.firmaAdi} · ${o.magazaAdi}`,
        meta: `${OTURUM_DURUM_LABELS[o.durum]} · ${o.tarih.slice(0, 10)}`,
        tone: this.toneForDurum(o.durum),
      }));
  });

  ngOnInit(): void {
    this.health.check().subscribe({
      next: (res) => this.healthState.set(res),
      error: () => this.healthState.set(null),
    });
    this.oSvc.list().subscribe({
      next: (r) => this.oturumlar.set(r),
      error: () => this.oturumlar.set([]),
    });
  }

  goNewOturum(): void {
    this.router.navigate(['/oturumlar']);
  }

  open(o: OturumList): void {
    this.router.navigate(['/oturumlar', o.id]);
  }

  durumLabel(d: OturumDurum): string { return OTURUM_DURUM_LABELS[d]; }
  durumChipClass(d: OturumDurum): string {
    switch (d) {
      case 'aktif': return 'is-blue';
      case 'kilitli': return 'is-amber';
      case 'tamamlandi': return 'is-green';
      case 'iptal': return 'is-coral';
      case 'excel_bekleniyor': return 'is-cyan';
      default: return 'is-violet';
    }
  }

  formatDate(iso: string): string { return iso.slice(0, 10); }
  formatRelative(iso: string): string {
    const d = new Date(iso);
    const diff = (Date.now() - d.getTime()) / 1000;
    if (diff < 60) return 'şimdi';
    if (diff < 3600) return `${Math.floor(diff / 60)} dk önce`;
    if (diff < 86400) return `${Math.floor(diff / 3600)} saat önce`;
    return `${Math.floor(diff / 86400)} gün önce`;
  }

  /** SVG gauge için dasharray — yarı çember (180°). 100 oran → tam yay. */
  arcLength = (oran: number): string => {
    const max = Math.PI * 80; // r=80
    const len = (Math.max(0, Math.min(100, oran)) / 100) * max;
    return `${len.toFixed(1)} ${(max - len).toFixed(1)}`;
  };

  private iconForDurum(d: OturumDurum): string {
    switch (d) {
      case 'aktif': return '◐';
      case 'kilitli': return '◔';
      case 'tamamlandi': return '✓';
      case 'iptal': return '×';
      default: return '◌';
    }
  }
  private toneForDurum(d: OturumDurum): 'violet' | 'cyan' | 'pink' | 'green' {
    switch (d) {
      case 'tamamlandi': return 'green';
      case 'aktif': return 'cyan';
      case 'iptal': return 'pink';
      default: return 'violet';
    }
  }
}
