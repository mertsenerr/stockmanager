import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ModalComponent } from '../../../shared/ui/modal/modal.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
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
      default: return 'is-violet';
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

  formatDate(iso: string): string { return iso.slice(0, 10); }

  private todayYmd(): string {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }
}
