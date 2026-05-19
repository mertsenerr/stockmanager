import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { ModalComponent } from '../../shared/ui/modal/modal.component';
import { ToastService } from '../../shared/ui/toast/toast.service';
import { AuthService } from '../../core/auth/auth.service';
import { Firma } from '../admin/admin.models';
import { FirmaService } from '../admin/firma.service';
import { BelgeTipiService } from './belge-tipi.service';
import { BelgeTipi, BelgeTipiUpsert, IMZA_ROL_OPTIONS, ImzaRolu } from './belge-tipi.models';

@Component({
  selector: 'app-belge-tipleri',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './belge-tipleri.component.html',
  styleUrls: ['./belge-tipleri.component.css'],
})
export class BelgeTipleriComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly svc = inject(BelgeTipiService);
  private readonly firmaSvc = inject(FirmaService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  readonly user = this.auth.currentUser;
  readonly isSistem = computed(() => this.user()?.rol === 'Sistem');

  readonly liste = signal<BelgeTipi[]>([]);
  readonly firmalar = signal<Firma[]>([]);
  readonly loading = signal(false);
  readonly showArchived = signal(false);
  readonly query = signal('');

  readonly editing = signal<BelgeTipi | null>(null);
  readonly modalOpen = signal(false);
  readonly saving = signal(false);
  readonly serverError = signal<string | null>(null);

  protected readonly imzaRolOptions = IMZA_ROL_OPTIONS;

  readonly form = this.fb.nonNullable.group({
    firmaId: [''],
    ad: ['', [Validators.required, Validators.maxLength(120)]],
    aciklama: ['', [Validators.maxLength(1000)]],
    sayimBaskaniImza: [false],
    magazaYetkilisiImza: [false],
    kaseGerekli: [false],
    arsivlendi: [false],
  });

  readonly filtered = computed(() => {
    const q = this.query().toLowerCase().trim();
    const items = this.liste();
    if (!q) return items;
    return items.filter(
      (t) =>
        t.ad.toLowerCase().includes(q) ||
        (t.firmaAdi ?? '').toLowerCase().includes(q) ||
        (t.aciklama ?? '').toLowerCase().includes(q),
    );
  });

  ngOnInit(): void {
    this.refresh();
    if (this.isSistem()) {
      this.firmaSvc.list().subscribe({
        next: (list) => this.firmalar.set(list),
        error: () => this.firmalar.set([]),
      });
    }
  }

  refresh(): void {
    this.loading.set(true);
    this.svc.list({ includeArchived: this.showArchived() }).subscribe({
      next: (items) => {
        this.liste.set(items);
        this.loading.set(false);
      },
      error: () => {
        this.toast.error('Belge tipleri yüklenemedi.');
        this.loading.set(false);
      },
    });
  }

  toggleArchived(): void {
    this.showArchived.update((v) => !v);
    this.refresh();
  }

  setQuery(value: string): void { this.query.set(value); }

  openCreate(): void {
    this.editing.set(null);
    this.serverError.set(null);
    this.form.reset({
      firmaId: '',
      ad: '',
      aciklama: '',
      sayimBaskaniImza: false,
      magazaYetkilisiImza: false,
      kaseGerekli: false,
      arsivlendi: false,
    });
    this.modalOpen.set(true);
  }

  openEdit(t: BelgeTipi): void {
    this.editing.set(t);
    this.serverError.set(null);
    this.form.reset({
      firmaId: t.firmaId,
      ad: t.ad,
      aciklama: t.aciklama ?? '',
      sayimBaskaniImza: t.gerekenImzaRolleri.includes('SayimBaskani'),
      magazaYetkilisiImza: t.gerekenImzaRolleri.includes('MagazaYetkilisi'),
      kaseGerekli: t.kaseGerekli,
      arsivlendi: t.arsivlendi,
    });
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
    this.editing.set(null);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    const roller: ImzaRolu[] = [];
    if (v.sayimBaskaniImza) roller.push('SayimBaskani');
    if (v.magazaYetkilisiImza) roller.push('MagazaYetkilisi');

    const payload: BelgeTipiUpsert = {
      firmaId: this.isSistem() ? (v.firmaId || null) : null,
      ad: v.ad.trim(),
      aciklama: v.aciklama.trim() ? v.aciklama.trim() : null,
      gerekenImzaRolleri: roller,
      kaseGerekli: v.kaseGerekli,
      arsivlendi: v.arsivlendi,
    };

    this.saving.set(true);
    this.serverError.set(null);
    const current = this.editing();
    const op$ = current ? this.svc.update(current.id, payload) : this.svc.create(payload);
    op$.subscribe({
      next: () => {
        this.toast.success(current ? 'Belge tipi güncellendi.' : 'Belge tipi eklendi.');
        this.saving.set(false);
        this.closeModal();
        this.refresh();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        const msg = (err.error && (err.error.message || err.error.title)) || 'İşlem başarısız.';
        this.serverError.set(msg);
      },
    });
  }

  archiveToggle(t: BelgeTipi): void {
    const payload: BelgeTipiUpsert = {
      firmaId: this.isSistem() ? t.firmaId : null,
      ad: t.ad,
      aciklama: t.aciklama,
      gerekenImzaRolleri: t.gerekenImzaRolleri,
      kaseGerekli: t.kaseGerekli,
      arsivlendi: !t.arsivlendi,
    };
    this.svc.update(t.id, payload).subscribe({
      next: () => {
        this.toast.success(t.arsivlendi ? 'Belge tipi geri yüklendi.' : 'Belge tipi arşivlendi.');
        this.refresh();
      },
      error: () => this.toast.error('İşlem başarısız.'),
    });
  }

  rolEtiketleri(t: BelgeTipi): string {
    if (t.gerekenImzaRolleri.length === 0) return 'İmza gerekmiyor';
    return t.gerekenImzaRolleri
      .map((r) => IMZA_ROL_OPTIONS.find((o) => o.value === r)?.label ?? r)
      .join(', ');
  }
}
