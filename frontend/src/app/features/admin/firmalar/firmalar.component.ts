import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ModalComponent } from '../../../shared/ui/modal/modal.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { ConfirmService } from '../../../shared/ui/confirm/confirm.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { FirmaService } from '../firma.service';
import { FIRMA_TIP_LABELS, FIRMA_TIP_OPTIONS, Firma, FirmaTip } from '../admin.models';

@Component({
  selector: 'app-firmalar',
  standalone: true,
  imports: [ReactiveFormsModule, ModalComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './firmalar.component.html',
})
export class FirmalarComponent implements OnInit {
  private readonly svc = inject(FirmaService);
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  readonly firmalar = signal<Firma[]>([]);
  readonly loading = signal(false);
  readonly query = signal('');
  readonly includeInactive = signal(false);
  readonly editing = signal<Firma | null>(null);
  readonly modalOpen = signal(false);
  readonly saving = signal(false);
  readonly serverError = signal<string | null>(null);

  readonly tipOptions = FIRMA_TIP_OPTIONS;

  readonly form = this.fb.nonNullable.group({
    ad: ['', [Validators.required, Validators.maxLength(120)]],
    kisaltma: ['', [Validators.pattern(/^[A-Za-z0-9]{3,6}$/)]],
    tip: ['diger' as FirmaTip, [Validators.required]],
    logoUrl: [''],
    aktifMi: [true],
  });

  readonly filtered = computed(() => {
    const q = this.query().toLowerCase().trim();
    return q === ''
      ? this.firmalar()
      : this.firmalar().filter((f) => f.ad.toLowerCase().includes(q));
  });

  readonly tipLabel = (tip: FirmaTip) => FIRMA_TIP_LABELS[tip];

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    this.svc.list(this.includeInactive()).subscribe({
      next: (res) => {
        this.firmalar.set(res);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Firmalar yüklenemedi.');
      },
    });
  }

  toggleInactive(): void {
    this.includeInactive.update((v) => !v);
    this.refresh();
  }

  openCreate(): void {
    this.editing.set(null);
    this.serverError.set(null);
    this.form.reset({ ad: '', kisaltma: '', tip: 'diger', logoUrl: '', aktifMi: true });
    this.modalOpen.set(true);
  }

  openEdit(firma: Firma): void {
    this.editing.set(firma);
    this.serverError.set(null);
    this.form.reset({
      ad: firma.ad,
      kisaltma: firma.kisaltma ?? '',
      tip: firma.tip,
      logoUrl: firma.logoUrl ?? '',
      aktifMi: firma.aktifMi,
    });
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  submit(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    const payload = {
      ...raw,
      kisaltma: raw.kisaltma ? raw.kisaltma.toUpperCase() : '',
      logoUrl: raw.logoUrl || null,
    };
    this.saving.set(true);
    this.serverError.set(null);

    const editing = this.editing();
    const op = editing
      ? this.svc.update(editing.id, payload)
      : this.svc.create(payload);

    op.subscribe({
      next: () => {
        this.saving.set(false);
        this.modalOpen.set(false);
        this.toast.success(editing ? 'Firma güncellendi.' : 'Firma oluşturuldu.');
        this.refresh();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.serverError.set(err.error?.message ?? 'İşlem başarısız.');
      },
    });
  }

  async remove(firma: Firma): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Firmayı sil',
      message: `${firma.ad} silinsin mi? Bu işlem geri alınabilir (pasifleştirme).`,
      confirmLabel: 'Sil',
      danger: true,
    });
    if (!ok) return;

    this.svc.remove(firma.id).subscribe({
      next: () => {
        this.toast.success('Firma silindi.');
        this.refresh();
      },
      error: (err: HttpErrorResponse) => {
        this.toast.error(err.error?.message ?? 'Silme başarısız.');
      },
    });
  }
}
