import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ModalComponent } from '../../../shared/ui/modal/modal.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ConfirmService } from '../../../shared/ui/confirm/confirm.service';
import { AuthService } from '../../../core/auth/auth.service';
import { FirmaService } from '../../admin/firma.service';
import { MagazaService } from '../../admin/magaza.service';
import { Firma, Magaza } from '../../admin/admin.models';
import { ArkadasService, Friend } from '../../arkadaslar/arkadas.service';
import { OturumService } from '../oturum.service';
import { OTURUM_DURUM_COLOR, OTURUM_DURUM_LABELS, OturumDurum, OturumList } from '../sayim.models';

@Component({
  selector: 'app-oturumlar',
  standalone: true,
  imports: [ReactiveFormsModule, ModalComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './oturumlar.component.html',
})
export class OturumlarComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly svc = inject(OturumService);
  private readonly fSvc = inject(FirmaService);
  private readonly mSvc = inject(MagazaService);
  private readonly arkadasSvc = inject(ArkadasService);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly oturumlar = signal<OturumList[]>([]);
  readonly firmalar = signal<Firma[]>([]);
  readonly magazalar = signal<Magaza[]>([]);
  readonly loading = signal(false);
  readonly query = signal('');
  readonly firmaFilter = signal<string>('');
  readonly durumFilter = signal<string>('');

  readonly modalOpen = signal(false);
  readonly saving = signal(false);
  readonly serverError = signal<string | null>(null);

  readonly friends = signal<Friend[]>([]);
  readonly selectedKatilimciIds = signal<string[]>([]);
  readonly katilimciQuery = signal('');

  readonly selectedKatilimcilar = computed(() => {
    const ids = new Set(this.selectedKatilimciIds());
    return this.friends().filter((f) => ids.has(f.kullaniciId));
  });

  readonly suggestedKatilimcilar = computed(() => {
    const q = this.katilimciQuery().toLowerCase().trim();
    const selected = new Set(this.selectedKatilimciIds());
    const pool = this.friends().filter((f) => !selected.has(f.kullaniciId));
    if (q === '') return pool.slice(0, 8);
    return pool
      .filter((f) =>
        f.adSoyad.toLowerCase().includes(q) || f.email.toLowerCase().includes(q),
      )
      .slice(0, 8);
  });

  readonly canCreate = computed(() => {
    const u = this.auth.currentUser();
    return u?.rol === 'Sistem' || u?.rol === 'SayimBaskani';
  });

  readonly durumLabel = (d: OturumDurum) => OTURUM_DURUM_LABELS[d];
  readonly durumColor = (d: OturumDurum) => OTURUM_DURUM_COLOR[d];
  readonly durumChipClass = (d: OturumDurum): string => {
    switch (d) {
      case 'aktif': return 'is-blue';
      case 'kilitli': return 'is-amber';
      case 'tamamlandi': return 'is-green';
      case 'iptal': return 'is-coral';
      case 'excel_bekleniyor': return 'is-cyan';
      default: return 'is-accent';
    }
  };

  readonly durumOptions: { value: OturumDurum | ''; label: string }[] = [
    { value: '', label: 'Tüm durumlar' },
    { value: 'taslak', label: 'Taslak' },
    { value: 'excel_bekleniyor', label: 'Excel bekleniyor' },
    { value: 'aktif', label: 'Aktif' },
    { value: 'kilitli', label: 'Kilitli' },
    { value: 'tamamlandi', label: 'Tamamlandı' },
    { value: 'iptal', label: 'İptal' },
  ];

  readonly form = this.fb.nonNullable.group({
    magazaId: ['', [Validators.required]],
    tarih: [this.todayYmd(), [Validators.required]],
  });

  readonly filtered = computed(() => {
    const q = this.query().toLowerCase().trim();
    const fid = this.firmaFilter();
    return this.oturumlar().filter((o) => {
      if (fid && o.firmaId !== fid) return false;
      if (!q) return true;
      return o.magazaAdi.toLowerCase().includes(q)
        || o.firmaAdi.toLowerCase().includes(q);
    });
  });

  /** Bulk-select state — set of oturum ids checked in the table. */
  readonly selectedIds = signal<Set<string>>(new Set());
  readonly deleting = signal<string | null>(null);
  readonly bulkDeleting = signal(false);

  readonly allFilteredSelected = computed(() => {
    const f = this.filtered();
    if (f.length === 0) return false;
    const sel = this.selectedIds();
    return f.every((o) => sel.has(o.id));
  });

  readonly someFilteredSelected = computed(() => {
    const sel = this.selectedIds();
    return this.filtered().some((o) => sel.has(o.id));
  });

  isSelected(id: string): boolean { return this.selectedIds().has(id); }

  toggleSelect(id: string, event?: Event): void {
    event?.stopPropagation();
    this.selectedIds.update((s) => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  toggleSelectAll(event?: Event): void {
    event?.stopPropagation();
    const all = this.allFilteredSelected();
    this.selectedIds.update((s) => {
      const next = new Set(s);
      const ids = this.filtered().map((o) => o.id);
      if (all) {
        for (const id of ids) next.delete(id);
      } else {
        for (const id of ids) next.add(id);
      }
      return next;
    });
  }

  clearSelection(): void { this.selectedIds.set(new Set()); }

  ngOnInit(): void {
    this.refresh();
    if (this.canCreate()) {
      this.fSvc.list(false).subscribe({
        next: (r) => this.firmalar.set(r),
        error: () => undefined,
      });
      this.mSvc.list({ includeInactive: false }).subscribe({
        next: (r) => this.magazalar.set(r),
        error: () => undefined,
      });
      this.arkadasSvc.list().subscribe({
        next: (r) => this.friends.set(r.arkadaslar),
        error: () => undefined,
      });
    }
  }

  addKatilimci(f: Friend): void {
    const cur = this.selectedKatilimciIds();
    if (cur.includes(f.kullaniciId)) return;
    this.selectedKatilimciIds.set([...cur, f.kullaniciId]);
    this.katilimciQuery.set('');
  }

  removeKatilimci(uid: string): void {
    this.selectedKatilimciIds.set(this.selectedKatilimciIds().filter((x) => x !== uid));
  }

  refresh(): void {
    this.loading.set(true);
    this.svc.list({ durum: this.durumFilter() || undefined }).subscribe({
      next: (r) => { this.oturumlar.set(r); this.loading.set(false); },
      error: () => { this.loading.set(false); this.toast.error('Oturumlar yüklenemedi.'); },
    });
  }

  setDurumFilter(v: string): void {
    this.durumFilter.set(v);
    this.refresh();
  }

  openCreate(): void {
    this.serverError.set(null);
    this.form.reset({ magazaId: '', tarih: this.todayYmd() });
    this.selectedKatilimciIds.set([]);
    this.katilimciQuery.set('');
    this.modalOpen.set(true);
  }

  closeModal(): void { this.modalOpen.set(false); }

  submit(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.serverError.set(null);
    this.svc.create({
      magazaId: v.magazaId,
      tarih: v.tarih,
      katilimcilar: this.selectedKatilimciIds().map((id) => ({
        kullaniciId: id,
        rol: 'Kullanici',
      })),
    }).subscribe({
      next: (created) => {
        this.saving.set(false);
        this.modalOpen.set(false);
        this.toast.success('Oturum oluşturuldu.');
        this.router.navigate(['/oturumlar', created.id]);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.serverError.set(err.error?.message ?? 'İşlem başarısız.');
      },
    });
  }

  open(o: OturumList): void {
    this.router.navigate(['/oturumlar', o.id]);
  }

  async deleteOne(o: OturumList, event?: Event): Promise<void> {
    event?.stopPropagation();
    if (this.deleting()) return;
    const ok = await this.confirm.ask({
      title: 'Oturumu sil',
      message: `${o.firmaAdi} · ${o.magazaAdi} (${this.formatDate(o.tarih)}) oturumu kalıcı olarak silinecek. Tüm satırlar, yorumlar ve dosyalar gider. Devam edilsin mi?`,
      confirmLabel: 'Kalıcı sil',
      cancelLabel: 'Vazgeç',
      danger: true,
    });
    if (!ok) return;
    this.deleting.set(o.id);
    this.svc.hardDelete(o.id).subscribe({
      next: () => {
        this.deleting.set(null);
        this.oturumlar.update((list) => list.filter((x) => x.id !== o.id));
        this.selectedIds.update((s) => {
          if (!s.has(o.id)) return s;
          const next = new Set(s); next.delete(o.id); return next;
        });
        this.toast.success('Oturum silindi.');
      },
      error: (err: HttpErrorResponse) => {
        this.deleting.set(null);
        this.toast.error(err.error?.message ?? 'Silme başarısız.');
      },
    });
  }

  async deleteSelected(): Promise<void> {
    if (this.bulkDeleting()) return;
    const ids = Array.from(this.selectedIds());
    if (ids.length === 0) return;
    const ok = await this.confirm.ask({
      title: `${ids.length} oturum silinsin mi?`,
      message: `Seçilen ${ids.length} oturum ve içerikleri kalıcı olarak silinecek. Bu işlem geri alınamaz.`,
      confirmLabel: 'Kalıcı sil',
      cancelLabel: 'Vazgeç',
      danger: true,
    });
    if (!ok) return;
    this.bulkDeleting.set(true);
    let success = 0;
    let failure = 0;
    await Promise.all(ids.map((id) => new Promise<void>((resolve) => {
      this.svc.hardDelete(id).subscribe({
        next: () => { success++; resolve(); },
        error: () => { failure++; resolve(); },
      });
    })));
    this.bulkDeleting.set(false);
    this.clearSelection();
    this.refresh();
    if (failure === 0) this.toast.success(`${success} oturum silindi.`);
    else this.toast.error(`${success} silindi, ${failure} başarısız.`);
  }

  formatDate(iso: string): string { return iso.slice(0, 10); }

  private todayYmd(): string {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }
}
