import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ConfirmService } from '../../../shared/ui/confirm/confirm.service';
import { AuthService } from '../../../core/auth/auth.service';
import { OturumService } from '../../sayim/oturum.service';
import { OturumList, OTURUM_DURUM_LABELS, OTURUM_DURUM_COLOR, OturumDurum } from '../../sayim/sayim.models';
import { RaporService } from '../rapor.service';
import { MagazaSapma, SaymanPerformans } from '../rapor.models';

type Tab = 'oturum' | 'sapma' | 'sayman';

@Component({
  selector: 'app-raporlar',
  standalone: true,
  imports: [PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './raporlar.component.html',
})
export class RaporlarComponent implements OnInit {
  private readonly svc = inject(RaporService);
  private readonly oSvc = inject(OturumService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly router = inject(Router);

  readonly canDelete = computed(() => {
    const r = this.auth.currentUser()?.rol;
    return r === 'Sistem' || r === 'SayimBaskani';
  });

  readonly tab = signal<Tab>('oturum');
  readonly oturumlar = signal<OturumList[]>([]);
  readonly sapma = signal<MagazaSapma[]>([]);
  readonly perf = signal<SaymanPerformans[]>([]);
  readonly loading = signal(false);
  readonly downloading = signal<string | null>(null);

  readonly canSeeReports = computed(() => {
    const r = this.auth.currentUser()?.rol;
    return r === 'Sistem' || r === 'SayimBaskani';
  });

  readonly durumLabel = (d: OturumDurum) => OTURUM_DURUM_LABELS[d];
  readonly durumColor = (d: OturumDurum) => OTURUM_DURUM_COLOR[d];

  ngOnInit(): void {
    this.refresh();
  }

  setTab(t: Tab): void {
    this.tab.set(t);
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    if (this.tab() === 'oturum') {
      this.oSvc.list().subscribe({
        next: (r) => { this.oturumlar.set(r); this.loading.set(false); },
        error: () => { this.loading.set(false); this.toast.error('Yüklenemedi.'); },
      });
    } else if (this.tab() === 'sapma') {
      this.svc.magazaSapma().subscribe({
        next: (r) => { this.sapma.set(r); this.loading.set(false); },
        error: () => { this.loading.set(false); this.toast.error('Yüklenemedi.'); },
      });
    } else {
      this.svc.saymanPerformans().subscribe({
        next: (r) => { this.perf.set(r); this.loading.set(false); },
        error: () => { this.loading.set(false); this.toast.error('Yüklenemedi.'); },
      });
    }
  }

  async downloadExcel(o: OturumList): Promise<void> {
    const token = this.auth.accessToken();
    if (!token) {
      this.toast.error('Oturum süresi dolmuş, tekrar giriş yapın.');
      return;
    }
    this.downloading.set(o.id);
    try {
      await this.svc.downloadOturumExcel(o.id, token, `sayim-${o.firmaAdi}-${o.magazaAdi}`);
      this.toast.success('Excel indirildi.');
    } catch {
      this.toast.error('İndirme başarısız.');
    } finally {
      this.downloading.set(null);
    }
  }

  open(o: OturumList): void {
    this.router.navigate(['/oturumlar', o.id]);
  }

  async cancelOturum(o: OturumList): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Oturumu kalıcı sil',
      message: `${o.firmaAdi} · ${o.magazaAdi} (${this.formatDate(o.tarih)}) oturumu KALICI olarak silinecek. Bu işlem geri alınamaz.`,
      confirmLabel: 'Kalıcı sil',
      danger: true,
    });
    if (!ok) return;
    this.oSvc.hardDelete(o.id).subscribe({
      next: () => {
        this.toast.success('Oturum silindi.');
        this.refresh();
      },
      error: (err: HttpErrorResponse) =>
        this.toast.error(err.error?.message ?? 'Silme başarısız.'),
    });
  }

  formatDate(iso: string): string { return iso.slice(0, 10); }
  formatDateTime(iso?: string | null): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleString('tr-TR');
  }
}
