import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ModalComponent } from '../../shared/ui/modal/modal.component';
import { PageHeaderComponent } from '../../shared/ui/page-header/page-header.component';
import { ConfirmService } from '../../shared/ui/confirm/confirm.service';
import { ToastService } from '../../shared/ui/toast/toast.service';
import { AuthService } from '../../core/auth/auth.service';
import { KullaniciService } from '../admin/kullanici.service';
import { KullaniciList } from '../admin/admin.models';
import { OzelRaporService } from './ozel-rapor.service';
import { OzelRapor, OzelRaporDosya } from './ozel-rapor.models';

const MAX_FILE_SIZE = 50 * 1024 * 1024;
const ALLOWED_EXT = ['.xlsx', '.xls', '.pdf'];

@Component({
  selector: 'app-ozel-raporlar',
  standalone: true,
  imports: [ReactiveFormsModule, ModalComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ozel-raporlar.component.html',
})
export class OzelRaporlarComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly svc = inject(OzelRaporService);
  private readonly kSvc = inject(KullaniciService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly user = this.auth.currentUser;
  readonly canManage = computed(() => {
    const r = this.user()?.rol;
    return r === 'Sistem' || r === 'SayimBaskani';
  });

  readonly raporlar = signal<OzelRapor[]>([]);
  readonly loading = signal(false);
  readonly query = signal('');

  readonly editing = signal<OzelRapor | null>(null);
  readonly modalOpen = signal(false);
  readonly saving = signal(false);
  readonly serverError = signal<string | null>(null);
  readonly uploading = signal<string | null>(null);
  readonly downloadingId = signal<string | null>(null);

  /** Raporlara erişim verilebilecek kendi kullanıcılarımız (yalnızca Kullanici rolündekiler). */
  readonly availableUsers = signal<KullaniciList[]>([]);

  readonly form = this.fb.nonNullable.group({
    ad: ['', [Validators.required, Validators.maxLength(160)]],
    aciklama: ['', [Validators.maxLength(2000)]],
    erisebilenKullaniciIds: this.fb.nonNullable.control<string[]>([]),
  });

  readonly filtered = computed(() => {
    const q = this.query().toLowerCase().trim();
    return q === ''
      ? this.raporlar()
      : this.raporlar().filter((r) => r.ad.toLowerCase().includes(q));
  });

  ngOnInit(): void {
    this.refresh();
    if (this.canManage()) {
      this.kSvc.list(false).subscribe({
        next: (r) => this.availableUsers.set(r.filter((u) => u.rol === 'Kullanici' && u.aktifMi)),
        error: () => undefined,
      });
    }
  }

  refresh(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (r) => { this.raporlar.set(r); this.loading.set(false); },
      error: () => { this.loading.set(false); this.toast.error('Raporlar yüklenemedi.'); },
    });
  }

  openCreate(): void {
    this.editing.set(null);
    this.serverError.set(null);
    this.form.reset({ ad: '', aciklama: '', erisebilenKullaniciIds: [] });
    this.modalOpen.set(true);
  }

  openEdit(r: OzelRapor): void {
    this.editing.set(r);
    this.serverError.set(null);
    this.form.reset({
      ad: r.ad,
      aciklama: r.aciklama ?? '',
      erisebilenKullaniciIds: [...r.erisebilenKullaniciIds],
    });
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  toggleAccess(uid: string): void {
    const cur = this.form.controls.erisebilenKullaniciIds.value;
    this.form.controls.erisebilenKullaniciIds.setValue(
      cur.includes(uid) ? cur.filter((x) => x !== uid) : [...cur, uid],
    );
  }

  isChecked(uid: string): boolean {
    return this.form.controls.erisebilenKullaniciIds.value.includes(uid);
  }

  submit(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.serverError.set(null);
    const v = this.form.getRawValue();
    const payload = {
      ad: v.ad.trim(),
      aciklama: v.aciklama?.trim() || null,
      erisebilenKullaniciIds: v.erisebilenKullaniciIds,
    };
    const editing = this.editing();
    const op = editing ? this.svc.update(editing.id, payload) : this.svc.create(payload);
    op.subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.modalOpen.set(false);
        this.toast.success(editing ? 'Rapor güncellendi.' : 'Rapor oluşturuldu.');
        // Yeni oluşturulanı listeye ekle / güncelle
        const list = this.raporlar();
        if (editing) {
          this.raporlar.set(list.map((r) => (r.id === saved.id ? saved : r)));
        } else {
          this.raporlar.set([saved, ...list]);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.serverError.set(err.error?.message ?? 'İşlem başarısız.');
      },
    });
  }

  async remove(r: OzelRapor): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Raporu sil',
      message: `"${r.ad}" ve içindeki tüm dosyalar kalıcı olarak silinecek. Devam edilsin mi?`,
      confirmLabel: 'Sil',
      danger: true,
    });
    if (!ok) return;
    this.svc.remove(r.id).subscribe({
      next: () => {
        this.toast.success('Rapor silindi.');
        this.raporlar.set(this.raporlar().filter((x) => x.id !== r.id));
      },
      error: (err: HttpErrorResponse) =>
        this.toast.error(err.error?.message ?? 'Silme başarısız.'),
    });
  }

  triggerUpload(r: OzelRapor, input: HTMLInputElement): void {
    input.value = '';
    input.click();
  }

  onFilesChosen(r: OzelRapor, ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const list = input.files;
    if (!list || list.length === 0) return;

    const files: File[] = [];
    for (let i = 0; i < list.length; i++) {
      const f = list.item(i);
      if (!f) continue;
      const ext = f.name.toLowerCase().slice(f.name.lastIndexOf('.'));
      if (!ALLOWED_EXT.includes(ext)) {
        this.toast.error(`${f.name}: sadece ${ALLOWED_EXT.join(', ')} yüklenebilir.`);
        return;
      }
      if (f.size > MAX_FILE_SIZE) {
        this.toast.error(`${f.name}: 50 MB sınırını aşıyor.`);
        return;
      }
      files.push(f);
    }
    if (files.length === 0) return;

    this.uploading.set(r.id);
    this.svc.uploadFiles(r.id, files).subscribe({
      next: (saved) => {
        this.uploading.set(null);
        this.toast.success(`${files.length} dosya yüklendi.`);
        this.raporlar.set(this.raporlar().map((x) => (x.id === saved.id ? saved : x)));
      },
      error: (err: HttpErrorResponse) => {
        this.uploading.set(null);
        this.toast.error(err.error?.message ?? 'Yükleme başarısız.');
      },
    });
  }

  async removeFile(r: OzelRapor, d: OzelRaporDosya): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Dosyayı sil',
      message: `"${d.ad}" silinsin mi?`,
      confirmLabel: 'Sil',
      danger: true,
    });
    if (!ok) return;
    this.svc.removeFile(r.id, d.id).subscribe({
      next: () => {
        this.toast.success('Dosya silindi.');
        this.raporlar.set(
          this.raporlar().map((x) =>
            x.id === r.id ? { ...x, dosyalar: x.dosyalar.filter((f) => f.id !== d.id) } : x,
          ),
        );
      },
      error: (err: HttpErrorResponse) =>
        this.toast.error(err.error?.message ?? 'Silme başarısız.'),
    });
  }

  async download(r: OzelRapor, d: OzelRaporDosya): Promise<void> {
    this.downloadingId.set(d.id);
    try {
      await this.svc.download(r.id, d.id, d.ad);
    } catch {
      this.toast.error('İndirme başarısız.');
    } finally {
      this.downloadingId.set(null);
    }
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  formatDate(iso: string | null | undefined): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleString('tr-TR');
  }
}
