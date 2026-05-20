import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ModalComponent } from '../../../shared/ui/modal/modal.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { ConfirmService } from '../../../shared/ui/confirm/confirm.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { KullaniciService } from '../kullanici.service';
import { FirmaService } from '../firma.service';
import { MagazaService } from '../magaza.service';
import { Firma, KullaniciList, Magaza } from '../admin.models';
import { ROLE_LABELS, UserRole } from '../../../core/auth/auth.models';
import { SelectComponent, SelectOption } from '../../../shared/ui/select/select.component';

@Component({
  selector: 'app-kullanicilar',
  standalone: true,
  imports: [ReactiveFormsModule, ModalComponent, PageHeaderComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './kullanicilar.component.html',
})
export class KullanicilarComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly svc = inject(KullaniciService);
  private readonly fSvc = inject(FirmaService);
  private readonly mSvc = inject(MagazaService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly users = signal<KullaniciList[]>([]);
  readonly firmalar = signal<Firma[]>([]);
  readonly magazalar = signal<Magaza[]>([]);
  readonly loading = signal(false);
  readonly query = signal('');
  readonly includeInactive = signal(false);

  readonly editing = signal<KullaniciList | null>(null);
  readonly modalOpen = signal(false);
  readonly saving = signal(false);
  readonly serverError = signal<string | null>(null);

  readonly roleOptions: { value: UserRole; label: string }[] = [
    { value: 'SayimBaskani', label: 'Sayım Başkanı' },
    { value: 'Kullanici', label: 'Kullanıcı' },
  ];
  readonly roleSelectOptions: SelectOption[] = this.roleOptions.map((o) => ({ value: o.value, label: o.label }));

  readonly roleLabel = (r: UserRole) => ROLE_LABELS[r];

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    adSoyad: ['', [Validators.required, Validators.maxLength(120)]],
    rol: ['Kullanici' as UserRole, [Validators.required]],
    password: [''],
    newPassword: [''],
    firmaIds: this.fb.nonNullable.control<string[]>([]),
    magazaIds: this.fb.nonNullable.control<string[]>([]),
    aktifMi: [true],
  });

  readonly filtered = computed(() => {
    const q = this.query().toLowerCase().trim();
    return q === ''
      ? this.users()
      : this.users().filter((u) =>
        u.adSoyad.toLowerCase().includes(q) || u.email.toLowerCase().includes(q),
      );
  });

  ngOnInit(): void {
    this.refresh();
    this.fSvc.list(false).subscribe({ next: (r) => this.firmalar.set(r) });
    this.mSvc.list({ includeInactive: false }).subscribe({ next: (r) => this.magazalar.set(r) });
  }

  refresh(): void {
    this.loading.set(true);
    this.svc.list(this.includeInactive()).subscribe({
      next: (r) => {
        this.users.set(r);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Kullanıcılar yüklenemedi.');
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
    this.form.reset({
      email: '', adSoyad: '', rol: 'Kullanici', password: '', newPassword: '',
      firmaIds: [], magazaIds: [], aktifMi: true,
    });
    this.form.controls.email.enable();
    this.form.controls.password.setValidators([Validators.required, Validators.minLength(8)]);
    this.form.controls.password.updateValueAndValidity();
    this.form.controls.newPassword.clearValidators();
    this.form.controls.newPassword.updateValueAndValidity();
    this.modalOpen.set(true);
  }

  openEdit(u: KullaniciList): void {
    this.editing.set(u);
    this.serverError.set(null);
    this.form.reset({
      email: u.email, adSoyad: u.adSoyad, rol: u.rol,
      password: '', newPassword: '',
      firmaIds: [...u.firmaIds],
      magazaIds: [...u.magazaIds],
      aktifMi: u.aktifMi,
    });
    this.form.controls.email.disable();
    this.form.controls.password.clearValidators();
    this.form.controls.password.updateValueAndValidity();
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  toggleId(arr: 'firmaIds' | 'magazaIds', id: string): void {
    const ctrl = this.form.controls[arr];
    const cur = ctrl.value;
    ctrl.setValue(cur.includes(id) ? cur.filter((x) => x !== id) : [...cur, id]);
  }

  isChecked(arr: 'firmaIds' | 'magazaIds', id: string): boolean {
    return this.form.controls[arr].value.includes(id);
  }

  submit(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.serverError.set(null);

    const v = this.form.getRawValue();
    const editing = this.editing();

    const op = editing
      ? this.svc.update(editing.id, {
        adSoyad: v.adSoyad.trim(),
        rol: v.rol,
        firmaIds: v.firmaIds,
        magazaIds: v.magazaIds,
        aktifMi: v.aktifMi,
        newPassword: v.newPassword || undefined,
      })
      : this.svc.create({
        email: v.email.trim().toLowerCase(),
        adSoyad: v.adSoyad.trim(),
        rol: v.rol,
        password: v.password,
        firmaIds: v.firmaIds,
        magazaIds: v.magazaIds,
        aktifMi: v.aktifMi,
      });

    op.subscribe({
      next: () => {
        this.saving.set(false);
        this.modalOpen.set(false);
        this.toast.success(editing ? 'Kullanıcı güncellendi.' : 'Kullanıcı oluşturuldu.');
        this.refresh();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.serverError.set(err.error?.message ?? 'İşlem başarısız.');
      },
    });
  }

  async remove(u: KullaniciList): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Kullanıcıyı sil',
      message: `${u.adSoyad} pasif duruma alınsın mı? Aktif oturumları sonlandırılacak.`,
      confirmLabel: 'Sil',
      danger: true,
    });
    if (!ok) return;
    this.svc.remove(u.id).subscribe({
      next: () => {
        this.toast.success('Kullanıcı silindi.');
        this.refresh();
      },
      error: (err: HttpErrorResponse) =>
        this.toast.error(err.error?.message ?? 'Silme başarısız.'),
    });
  }
}
